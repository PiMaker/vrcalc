using System;
using System.Globalization;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;

// TODO!
// Resizable columns/rows
// Auto-extend
// Auto-function button for SUM, etc.
// Copy/Paste in general
// Pickupable UI
// Stretch-Goal: Graphs???

namespace pi.vrcalc
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CellEvaluator : UdonSharpBehaviour
    {
        [SerializeField] private DebugLogger _log;
        [SerializeField] private CellHolder _cellHolder;
        [SerializeField] private Synchronizer _synchronizer;

        private DataList _evalQueue;

        private readonly DataError Err_ParsingFailed = DataError.UnableToParse;
        private readonly DataError Err_Recursion = DataError.ValueUnsupported;

        private const int MAX_LENGTH = 10;

        private void Start()
        {
            InitOperators();
        }

        public void EvaluateCell(DataDictionary cellData, bool reparse)
        {
            var cellFormula = cellData["formula"].String;

            // reparse immediately
            if (cellFormula.StartsWith("="))
            {
                if (reparse || cellData["executionGraph"].IsEmpty)
                {
                    var graph = ParseFormula(cellFormula);
                    if (graph == null || _parseError != null)
                    {
                        cellData["executionGraph"] = new DataToken(Err_ParsingFailed);
                        RefreshDependencies(cellData, null);
                    }
                    else
                    {
                        if (!RefreshDependencies(cellData, graph))
                            cellData["executionGraph"] = new DataToken(Err_Recursion);
                        else
                            cellData["executionGraph"] = graph;
                    }
                }
            }
            else if (reparse)
            {
                cellData["executionGraph"] = new DataToken();
                RefreshDependencies(cellData, null);
            }

            ((GameObject)cellData["formulaIndicator"].Reference).SetActive(cellData["executionGraph"].TokenType == TokenType.DataDictionary);

            // evalute delayed via queue
            if (_evalQueue == null)
                _evalQueue = new DataList();
            else
                _evalQueue.Remove(cellData); // remove if already in queue, reshuffle to the end
            _evalQueue.Add(cellData);
        }

        private void LateUpdate()
        {
            if (_evalQueue != null && _evalQueue.Count > 0)
            {
                var cellData = _evalQueue[0].DataDictionary;
                _evalQueue.RemoveAt(0);

                var graph = cellData["executionGraph"];
                string result;

                if (graph.IsEmpty)
                {
                    result = cellData["formula"].String; // not a formula, just return the literal value
                }
                else if (graph.TokenType != TokenType.DataDictionary)
                {
                    if (graph.TokenType == TokenType.Error)
                        result = graph.Error == Err_ParsingFailed ? "#FORMULA" : "#RECURS";
                    else
                        result = "#ERROR";
                }
                else
                {
                    result = EvaluateGraph(graph.DataDictionary, cellData); // run the evaluator, the expensive stuff
                    if (_evalError != null || result == null)
                        result = _evalError ?? "#ERROR";
                }

                cellData["value"] = result; // update value cache

                var text = (Text)cellData["text"].Reference;
                text.text = result;
                text.enabled = !string.IsNullOrWhiteSpace(result);

                _synchronizer.TriggerCellValueCallbacks(cellData["path"].String);

                // Enqueue dependents
                var dependedBy = cellData["dependedBy"].DataList;
                var dependedByCount = dependedBy.Count;
                for (int i = 0; i < dependedByCount; i++)
                {
                    var depPath = dependedBy[i].String;
                    if (_cellHolder.CellsPath.TryGetValue(depPath, out var depCellData))
                    {
                        EvaluateCell(depCellData.DataDictionary, reparse: false);
                    }
                }
            }
        }

        /*

            Graph data structure:

            Node:
            {
                "type": "constant" | "cell" | "range" | "relative" | "relativerange" | "function",
                "value": double (constant) | string (cell or range address) | string (function name),
                "children": DataList<Node> (function) | null (constant, range),
                "offsetX": in (relative, relativerange),
                "offsetY": int (relative, relativerange),
                "offsetX2": int (relativerange),
                "offsetY2": int (relativerange),

                "tempResult": RESERVED, // used during evaluation
            }

        */

#region Parsing

        private string _formula;
        private int _i;

        private bool _rejected;
        private string _parseError;
        public string ParseError() => _parseError;

        public DataDictionary ParseFormula(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula))
            {
                Reject("Formula is empty or whitespace");
                return null;
            }

            formula = formula.Trim();
            if (formula.StartsWith("="))
                formula = formula.Substring(1); // remove leading '='
            formula = formula.ToUpper(); // normalize to uppercase

            if (string.IsNullOrEmpty(formula) || formula.Length < 1)
            {
                Reject("Formula is empty after normalization");
                return null;
            }

            _formula = formula;
            _rejected = false;
            _parseError = null;

            _i = 0;
            var node = ReadNode(true);
            if (_rejected || node == null)
                return null;

            if (_i < formula.Length)
            {
                Reject($"Unexpected characters at end of formula: '{_formula.Substring(_i)}'");
                return null;
            }

            return node;
        }

        [RecursiveMethod]
        private DataDictionary ReadNode(bool allowOperator)
        {
            if (_rejected)
                return null;

            SkipWhitespace();

            if (_i >= _formula.Length)
            {
                Reject("Unexpected end of formula");
                return null;
            }

            //Debug.Log($"ReadNode at index {_i}, remain '{_formula.Substring(_i)}'");

            var d = new DataDictionary();
            var c = _formula[_i];

            if (c == '-' || c == '+')
            {
                // unary op
                if (_i + 1 >= _formula.Length)
                {
                    Reject($"Unexpected end of formula after unary operator '{c}'");
                    return null;
                }
                if (_formula[_i + 1] == '-' || _formula[_i + 1] == '+')
                {
                    Reject($"Unexpected unary operator '{_formula[_i + 1]}' after unary operator '{c}'");
                    return null;
                }

                _i++; // skip operator

                if (c == '+')
                {
                    // unary plus is a no-op, skip it
                    // FALLTHROUGH
                    c = _formula[_i];
                }
                else
                {
                    d["type"] = "function";
                    d["value"] = "NEGATE";

                    // don't allow operator after unary minus content so we only bind to the next expression,
                    // we instead continue operator parsing below after outputting the NEGATE node
                    var child = ReadNode(false);
                    if (_rejected || child == null)
                        return null;

                    var children = new DataList();
                    children.Add(child);
                    d["children"] = children;

                    if (allowOperator && _i < _formula.Length && IsOperator(_formula[_i]))
                    {
                        d = ReadNodeOperator(d);
                    }

                    return d;
                    // END
                }
            }

            if ((c >= '0' && c <= '9') || c == '.')
            {
                // constant
                var start = _i;
                while (_i < _formula.Length && ((_formula[_i] >= '0' && _formula[_i] <= '9') || _formula[_i] == '.'))
                {
                    _i++;
                }
                var end = _i;

                if (_i < _formula.Length && _formula[_i] == 'E')
                {
                    // scientific notation
                    _i++;
                    if (_i < _formula.Length && (_formula[_i] == '+' || _formula[_i] == '-'))
                    {
                        _i++;
                    }
                    while (_i < _formula.Length && _formula[_i] >= '0' && _formula[_i] <= '9')
                    {
                        _i++;
                    }
                    end = _i;
                }

                SkipWhitespace();

                if (_i >= _formula.Length)
                {
                    // end of formula, return constant node
                    var str = _formula.Substring(start, end - start);
                    if (double.TryParse(str, out double constantValue))
                    {
                        d["type"] = "constant";
                        d["value"] = constantValue;
                    }
                    else
                    {
                        Reject($"Failed to parse constant '{str}' at end of formula");
                        return null;
                    }
                    return d;
                }

                var endChar = _formula[_i];
                if (IsOperator(endChar) || endChar == ')' || endChar == ',')
                {
                    // valid end of constant, return constant node
                    var str = _formula.Substring(start, _i - start);
                    if (double.TryParse(str, out double constantValue))
                    {
                        d["type"] = "constant";
                        d["value"] = constantValue;

                        if (allowOperator && IsOperator(endChar))
                        {
                            d = ReadNodeOperator(d);
                        }
                    }
                    else
                    {
                        Reject($"Failed to parse constant '{str}'");
                        return null;
                    }

                    return d;
                }
                else
                {
                    Reject($"Invalid character after constant: '{endChar}'");
                    return null;
                }

                // END
            }

            if (c >= 'A' && c <= 'W')
            {
                // cell or range (one letter, 1+ digits)
                if (_i + 1 < _formula.Length && _formula[_i + 1] >= '0' && _formula[_i + 1] <= '9')
                {
                    var start = _i;
                    do
                    {
                        _i++;
                    }
                    while (_i < _formula.Length && _formula[_i] >= '0' && _formula[_i] <= '9');

                    var cellPath = _formula.Substring(start, _i - start);

                    SkipWhitespace();

                    if (_i + 2 < _formula.Length && _formula[_i] == ':' &&
                        _formula[_i + 1] >= 'A' && _formula[_i + 1] <= 'W' &&
                        _formula[_i + 2] >= '0' && _formula[_i + 2] <= '9')
                    {
                        // range
                        _i++; // skip ':'
                        var secondStart = _i;
                        do
                        {
                            _i++;
                        }
                        while (_i < _formula.Length && _formula[_i] >= '0' && _formula[_i] <= '9');

                        var secondCellPath = _formula.Substring(secondStart, _i - secondStart);

                        d["type"] = "range";
                        d["value"] = $"{cellPath}:{secondCellPath}";

                        if (allowOperator && _i < _formula.Length && IsOperator(_formula[_i]))
                        {
                            d = ReadNodeOperator(d);
                        }
                    }
                    else
                    {
                        // single cell at end of formula or parameter
                        d["type"] = "cell";
                        d["value"] = cellPath;

                        if (allowOperator && _i < _formula.Length && IsOperator(_formula[_i]))
                        {
                            d = ReadNodeOperator(d);
                        }
                    }

                    return d;
                    // END
                }

                // FALLTHROUGH, could be function
            }

            if (c == 'X' || c == 'Y')
            {
                // relative addressing: X1, Y-2, X4Y8
                var firstNode = ParseRelativeAddressNode(d, c);
                if (_rejected)
                    return null;
                
                if (firstNode != null)
                {
                    // check for range
                    SkipWhitespace();
                    if (_i + 1 < _formula.Length && _formula[_i] == ':')
                    {
                        _i++; // skip ':'
                        SkipWhitespace();
                        if (_i >= _formula.Length)
                        {
                            Reject("Unexpected end of formula after ':' in relative range");
                            return null;
                        }

                        var secondC = _formula[_i];
                        var secondIStart = _i;
                        var secondNode = ParseRelativeAddressNode(new DataDictionary(), secondC);
                        if (_rejected)
                            return null;
                        
                        if (secondNode == null)
                        {
                            Reject($"Expected relative address after ':' in relative range, got '{_formula.Substring(secondIStart)}'");
                            return null;
                        }

                        d = firstNode;
                        d["type"] = "relativerange";
                        d["offsetX2"] = secondNode["offsetX"];
                        d["offsetY2"] = secondNode["offsetY"];

                        if (d["offsetX2"].Int < d["offsetX"].Int)
                        {
                            var temp = d["offsetX2"];
                            d["offsetX2"] = d["offsetX"];
                            d["offsetX"] = temp;
                        }
                        if (d["offsetY2"].Int < d["offsetY"].Int)
                        {
                            var temp = d["offsetY2"];
                            d["offsetY2"] = d["offsetY"];
                            d["offsetY"] = temp;
                        }
                    }
                    else
                    {
                        d = firstNode;
                    }

                    if (allowOperator && _i < _formula.Length && IsOperator(_formula[_i]))
                    {
                        d = ReadNodeOperator(d);
                    }
                    return d;
                }
            }

            if (c >= 'A' && c <= 'Z')
            {
                // function (must not contain numbers in name)
                var start = _i;
                while (_i < _formula.Length && _formula[_i] >= 'A' && _formula[_i] <= 'Z')
                {
                    _i++;
                }

                SkipWhitespace();

                if (_i >= _formula.Length || _formula[_i] != '(')
                {
                    Reject("Expected '(' after function name");
                    return null;
                }

                var functionName = _formula.Substring(start, _i - start);
                _i++; // skip '('

                var children = new DataList();
                while (_i < _formula.Length && _formula[_i] != ')')
                {
                    var childNode = ReadNode(true);
                    if (_rejected || childNode == null)
                        return null;

                    children.Add(childNode);

                    SkipWhitespace();

                    if (_i < _formula.Length && _formula[_i] == ',')
                    {
                        _i++; // skip ','
                    }
                    else if (_i < _formula.Length && _formula[_i] != ')')
                    {
                        Reject("Expected ',' or ')' after function argument");
                        return null;
                    }
                }

                if (_i >= _formula.Length || _formula[_i] != ')')
                {
                    Reject("Expected ')' to close function arguments");
                    return null;
                }

                _i++; // skip ')'

                d["type"] = "function";
                d["value"] = functionName;
                d["children"] = children;

                if (allowOperator && _i < _formula.Length && IsOperator(_formula[_i]))
                {
                    d = ReadNodeOperator(d);
                }

                return d;

                // END
            }

            if (c == '(')
            {
                // empty parantheses, special cased as `ID` function
                _i++; // skip '('

                if (_i >= _formula.Length)
                {
                    Reject("Expected ')' after '('");
                    return null;
                }

                if (_formula[_i] == ')')
                {
                    Reject("Empty parantheses are not allowed");
                    return null;
                }

                var children = new DataList();
                var childNode = ReadNode(true);
                if (_rejected || childNode == null)
                    return null;

                children.Add(childNode);

                SkipWhitespace();

                if (_i < _formula.Length && _formula[_i] == ',')
                {
                    Reject("Unexpected ',' in ordering parantheses");
                    return null;
                }
                else if (_i < _formula.Length && _formula[_i] != ')')
                {
                    Reject("Expected ')' after parantheses");
                    return null;
                }
                else if (_i >= _formula.Length)
                {
                    Reject("Unexpected end of formula. Missing ')'?");
                    return null;
                }

                _i++; // skip ')'

                d["type"] = "function";
                d["value"] = "ID";
                d["children"] = children;

                if (allowOperator && _i < _formula.Length && IsOperator(_formula[_i]))
                {
                    d = ReadNodeOperator(d);
                }

                return d;
                // END
            }

            Reject($"Unexpected character '{c}' in formula");
            return null;
        }

        private DataDictionary ParseRelativeAddressNode(DataDictionary d, char c)
        {
            bool firstIsNegative = false;
            if (_i + 1 < _formula.Length && _formula[_i + 1] == '-')
            {
                firstIsNegative = true;
                _i++;
            }

            if (_i + 1 < _formula.Length && _formula[_i + 1] >= '0' && _formula[_i + 1] <= '9')
            {
                var start = _i;
                do
                {
                    _i++;
                }
                while (_i < _formula.Length && _formula[_i] >= '0' && _formula[_i] <= '9');

                var firstOffsetStr = _formula.Substring(start + 1, _i - start - 1);
                if (!int.TryParse(firstOffsetStr, out int firstOffset))
                {
                    Reject($"Failed to parse relative offset '{firstOffsetStr}'");
                    return null;
                }

                if (firstIsNegative)
                    firstOffset = -firstOffset;

                int secondOffset = int.MinValue;
                if (_i < _formula.Length)
                {
                    char c2 = _formula[_i];
                    bool secondIsNegative = false;
                    if (_i + 1 < _formula.Length && _formula[_i + 1] == '-')
                    {
                        secondIsNegative = true;
                        _i++;
                    }

                    if (_i + 1 < _formula.Length && (c2 == 'X' || c2 == 'Y') && _formula[_i + 1] >= '0' && _formula[_i + 1] <= '9')
                    {
                        var secondStart = _i;
                        do
                        {
                            _i++;
                        }
                        while (_i < _formula.Length && _formula[_i] >= '0' && _formula[_i] <= '9');

                        var secondOffsetStr = _formula.Substring(secondStart + 1, _i - secondStart - 1);
                        if (!int.TryParse(secondOffsetStr, out secondOffset))
                        {
                            Reject($"Failed to parse second relative offset '{secondOffsetStr}'");
                            return null;
                        }

                        if (secondIsNegative ? c == _formula[secondStart - 1] : c == _formula[secondStart])
                        {
                            Reject("Relative offset cannot have the same axis twice");
                            return null;
                        }

                        if (secondIsNegative)
                            secondOffset = -secondOffset;
                    }
                }

                if (firstOffset == 0 && (secondOffset == 0 || secondOffset == int.MinValue))
                {
                    Reject("Relative address cannot refer to itself");
                    return null;
                }

                d["type"] = "relative";
                if (secondOffset != int.MinValue)
                {
                    if (c == 'X')
                    {
                        d["offsetX"] = firstOffset;
                        d["offsetY"] = secondOffset;
                    }
                    else
                    {
                        d["offsetX"] = secondOffset;
                        d["offsetY"] = firstOffset;
                    }
                }
                else
                {
                    if (c == 'X')
                    {
                        d["offsetX"] = firstOffset;
                        d["offsetY"] = 0;
                    }
                    else
                    {
                        d["offsetX"] = 0;
                        d["offsetY"] = firstOffset;
                    }
                }

                return d;
            }
            else
            {
                _i--;
                return null;
            }
        }

        /*
            Algorithm from Wikipedia:

            parse_expression_1(lhs, min_precedence)
                lookahead := peek next token
                while lookahead is a binary operator whose precedence is >= min_precedence
                    op := lookahead
                    advance to next token
                    rhs := parse_primary ()
                    lookahead := peek next token
                    while lookahead is a binary operator whose precedence is greater
                            than op's, or a right-associative operator
                            whose precedence is equal to op's
                        rhs := parse_expression_1 (rhs, precedence of op + (1 if lookahead precedence is greater, else 0))
                        lookahead := peek next token
                    lhs := the result of applying op with operands lhs and rhs
                return lhs
        */
        [RecursiveMethod]
        private DataDictionary ReadNodeOperator(DataDictionary lhs, int minPrecedence = 0)
        {
            if (_rejected || _i >= _formula.Length)
                return null;

            var lookahead = _formula[_i];
            while (_i < _formula.Length && IsOperator(lookahead) && GetOperatorPrecedence(lookahead) >= minPrecedence)
            {
                var op = HandleGreaterLessEqualOperator(lookahead);
                _i++;

                var rhs = ReadNode(false);
                if (_rejected || rhs == null) return null;

                SkipWhitespace();
                if (_i < _formula.Length)
                {
                    lookahead = HandleGreaterLessEqualOperator(_formula[_i]);

                    var opPrecedence = GetOperatorPrecedence(op);
                    var laPrecedence = GetOperatorPrecedence(lookahead);
                    while (IsOperator(lookahead) && (laPrecedence > opPrecedence || (IsRightAssociative(lookahead) && laPrecedence == opPrecedence)))
                    {
                        rhs = ReadNodeOperator(rhs, opPrecedence + (laPrecedence > opPrecedence ? 1 : 0));
                        if (_rejected || rhs == null) return null;

                        SkipWhitespace();
                        if (_i >= _formula.Length)
                            break;

                        lookahead = HandleGreaterLessEqualOperator(_formula[_i]);
                        laPrecedence = GetOperatorPrecedence(lookahead);
                    }
                }

                var constructed = new DataDictionary();
                constructed["type"] = "function";
                constructed["value"] = op.ToString();
                var children = new DataList();
                children.Add(lhs);
                children.Add(rhs);
                constructed["children"] = children;
                lhs = constructed;
            }

            return lhs;
        }

        private DataDictionary _operatorChars;
        public void InitOperators()
        {
            _operatorChars = new DataDictionary();
            _operatorChars[(int)'<'] = 1;
            _operatorChars[(int)'>'] = 1;
            _operatorChars[(int)'≥'] = 1;
            _operatorChars[(int)'≤'] = 1;
            _operatorChars[(int)'='] = 1;
            _operatorChars[(int)'+'] = 2;
            _operatorChars[(int)'-'] = 2;
            _operatorChars[(int)'*'] = 3;
            _operatorChars[(int)'/'] = 3;
            _operatorChars[(int)'%'] = 3;
            _operatorChars[(int)'^'] = 4;
        }

        private bool IsOperator(char c)
        {
            return _operatorChars.ContainsKey((int)c);
        }

        private int GetOperatorPrecedence(char op)
        {
            if (_operatorChars.TryGetValue((int)op, out var precedence))
                return precedence.Int;
            return 0; // shouldn't happen
        }

        private bool IsRightAssociative(char op)
        {
            return op == '^';
        }

        private char HandleGreaterLessEqualOperator(char op)
        {
            if (op == '<' && _i + 1 < _formula.Length && _formula[_i + 1] == '=')
            {
                _i++;
                return '≤';
            }
            else if (op == '>' && _i + 1 < _formula.Length && _formula[_i + 1] == '=')
            {
                _i++;
                return '≥';
            }
            else
            {
                return op;
            }
        }

        private void SkipWhitespace()
        {
            while (_i < _formula.Length && char.IsWhiteSpace(_formula[_i]))
            {
                _i++;
            }
        }

        private void Reject(string error)
        {
            Log($"Invalid formula '{_formula}': {error}");
            _parseError = error;
            _rejected = true;
        }

        [RecursiveMethod]
        public static string DebugPrintGraph(DataDictionary node, int indent = 0)
        {
            if (node == null) return "null";

            var type = node["type"].String;
            var value = node["value"];
            var children = node.ContainsKey("children") ? node["children"].DataList : null;

            var indentStr = new string(' ', indent);
            var result = $"{indentStr}Node({type}, '{value}'";

            if (children != null && children.Count > 0)
            {
                result += ", children:\n";
                foreach (var child in children)
                {
                    result += DebugPrintGraph(child.DataDictionary, indent + 2) + "\n";
                }
                result += $"{indentStr})";
            }
            else
            {
                result += ")";
            }

            return result;
        }

#endregion

#region Evaluation

        private DataList _evalStack;
        private int _evalIdx;
        private string _evalError;

        // non-recursive for U# performance
        public string EvaluateGraph(DataDictionary node, DataDictionary cell)
        {
            _evalError = null;
            if (node == null) return "#NULL";

            // shortcuts
            if (node["type"].String == "constant")
                return CellHelpers.FormatDouble(node["value"].Double, MAX_LENGTH);

            if (node["type"].String == "cell")
            {
                var path = node["value"].String;
                var value = GetNodeValue(path);
                if (value == null)
                    return "#REF1";
                return AsDoubleIfParsing(value);
            }

            if (node["type"].String == "relative")
            {
                var offsetX = node["offsetX"].Int;
                var offsetY = node["offsetY"].Int;
                var value = GetNodeValueRelative(offsetX, offsetY, cell);
                if (value == null)
                    return "#REFR";
                return AsDoubleIfParsing(value);
            }

            if (node["type"].String == "range" || node["type"].String == "relativerange")
                return "#ERROR"; // range as root is not supported

            // below here: we know root node is "function"

            if (node["type"].String != "function")
            {
                return "#TYPE";
            }

            if (_evalStack == null)
                _evalStack = new DataList();

            _evalIdx = 0; // _evalStack.Clear();
            int childIndex = 0;
            var args = new DataList();

            while (node != null)
            {
                var functionName = node["value"].String;
                var isConditional = functionName == "IF";

                var children = node["children"].DataList;
                var childCount = children.Count;
                for (; childIndex < childCount; childIndex++)
                {
                    if (isConditional && childIndex > 0)
                    {
                        if (childCount != 3)
                        {
                            return "#IFARGS";
                        }

                        var conditionResult = GetDoubleValue1(args[0].DataDictionary, cell);
                        if (_evalError != null)
                            return _evalError;
                        
                        if (conditionResult == 0d && childIndex == 1)
                        {
                            // false, skip to else branch
                            childIndex++;
                        }
                        else if (childIndex == 2)
                        {
                            // true, force early exit
                            childCount--;
                            break;
                        }
                    }

                    var child = children[childIndex].DataDictionary;
                    if (child.TryGetValue("tempResult", out var tempResult))
                    {
                        // we came back here from a deeper evaluation
                        args.Add(tempResult);
                        child.Remove("tempResult");
                    }
                    else if (child["type"].String == "function")
                    {
                        Push(node);
                        Push(childIndex);
                        Push(args);
                        node = child;
                        childIndex = 0;
                        args = new DataList();
                        break;
                    }
                    else
                    {
                        args.Add(child);
                    }
                }

                if (childIndex < childCount)
                    continue; // we broke out of the for loop to go deeper

                // evaluate the function with the collected args
                var result = EvaluateFunction(functionName, args, cell);

                if (_evalError != null)
                {
                    // error, stop evaluation
                    return _evalError;
                }

                if (_evalIdx <= 0)
                {
                    // we are done, evaluate as non-function again
                    if (result["type"].String == "constant")
                        return CellHelpers.FormatDouble(result["value"].Double, MAX_LENGTH);

                    if (result["type"].String == "cell")
                    {
                        var path = result["value"].String;
                        var value = GetNodeValue(path);
                        if (value == null)
                            return "#REF1";
                        return value;
                    }

                    if (result["type"].String == "relative")
                    {
                        var offsetX = result["offsetX"].Int;
                        var offsetY = result["offsetY"].Int;
                        var value = GetNodeValueRelative(offsetX, offsetY, cell);
                        if (value == null)
                            return "#REFR";
                        return value;
                    }

                    return "#TYPE"; // invalid root type
                }

                node["tempResult"] = result;

                // back up the stack
                args = Pop().DataList;
                childIndex = Pop().Int;
                node = Pop().DataDictionary;
            }

            return "#ERROR"; // should not be reachable
        }

        private void Push(DataToken node)
        {
            if (_evalStack.Count <= _evalIdx)
                _evalStack.Add(node);
            else
                _evalStack[_evalIdx] = node;
            _evalIdx++;
        }

        private DataToken Pop()
        {
            if (_evalIdx <= 0)
                return new DataToken();

            _evalIdx--;
            var node = _evalStack[_evalIdx];
            return node;
        }

        private string AsDoubleIfParsing(string str)
        {
            if (double.TryParse(str, out double value))
                return CellHelpers.FormatDouble(value, MAX_LENGTH);
            return str;
        }

        private DataDictionary EvaluateFunction(string functionName, DataList args, DataDictionary cell)
        {
            switch (functionName)
            {
                case "ID":
                    if (args.Count != 1)
                    {
                        Fail("#ARGS");
                        return null;
                    }
                    return args[0].DataDictionary;
                case "IF":
                    // evaluated in the main loop to allow short-circuiting, here we get:
                    // args[0] = condition result
                    // args[1] = the chosen branch
                    if (args.Count != 2)
                    {
                        Fail("#IFARGS");
                        return null;
                    }
                    return args[1].DataDictionary;
                case "+":
                case "-":
                case "*":
                case "/":
                case "%":
                case "^":
                case "<":
                case ">":
                case "=":
                case "≤":
                case "≥":
                    var result = EvaluateOperator(functionName, args, cell);
                    if (_evalError != null)
                        return null;
                    var constant = new DataDictionary();
                    constant["type"] = "constant";
                    constant["value"] = result;
                    return constant;
                case "SUM":
                case "AVG":
                case "MEAN":
                case "COUNT":
                case "MIN":
                case "MAX":
                    var varargsResult = EvaluateVarargsFunction(functionName, args, cell);
                    if (_evalError != null)
                        return null;
                    var constantVarargs = new DataDictionary();
                    constantVarargs["type"] = "constant";
                    constantVarargs["value"] = varargsResult;
                    return constantVarargs;
                case "PI":
                    if (args.Count != 0)
                    {
                        Fail("#ARGS");
                        return null;
                    }
                    var constantPi = new DataDictionary();
                    constantPi["type"] = "constant";
                    constantPi["value"] = Math.PI;
                    return constantPi;
                case "E":
                    if (args.Count != 0)
                    {
                        Fail("#ARGS");
                        return null;
                    }
                    var constantE = new DataDictionary();
                    constantE["type"] = "constant";
                    constantE["value"] = Math.E;
                    return constantE;
                case "WEED":
                    if (args.Count != 0)
                    {
                        Fail("#ARGS");
                        return null;
                    }
                    var constantWeed = new DataDictionary();
                    constantWeed["type"] = "constant";
                    constantWeed["value"] = 420d;
                    return constantWeed;
                default:
                    var basicMathParam = args.Count >= 1 ? GetDoubleValue1(args[0].DataDictionary, cell) : 0d;
                    var basicMathResult = EvaluateBaseMathFunction(functionName, basicMathParam, out bool found);
                    if (found)
                    {
                        if (args.Count > 1)
                        {
                            Fail("#ARGS");
                            return null;
                        }

                        // minor rounding workaround
                        if (Math.Abs(basicMathResult) < 1e-10)
                            basicMathResult = 0d;

                        var constantMathBasic = new DataDictionary();
                        constantMathBasic["type"] = "constant";
                        constantMathBasic["value"] = basicMathResult;
                        return constantMathBasic;
                    }
                    break;
            }

            Log($"Evaluate: Unknown function '{functionName}'");
            Fail($"#FUNC");
            return null; // unknown function
        }

        private double EvaluateOperator(string op, DataList args, DataDictionary cell)
        {
            if (args.Count != 2)
            {
                Fail("#ARGS");
                return 0d;
            }

            var left = args[0].DataDictionary;
            var right = args[1].DataDictionary;

            double leftValue = GetDoubleValue1(left, cell);
            if (_evalError != null)
                return 0d;

            double rightValue = GetDoubleValue1(right, cell);
            if (_evalError != null)
                return 0d;

            switch (op)
            {
                case "+": return leftValue + rightValue;
                case "-": return leftValue - rightValue;
                case "*": return leftValue * rightValue;
                case "^": return Math.Pow(leftValue, rightValue);
                case "/":
                    if (rightValue == 0)
                    {
                        Fail("#DIV/0");
                        return 0d;
                    }
                    return leftValue / rightValue;
                case "%":
                    if (rightValue == 0)
                    {
                        Fail("#MOD/0");
                        return 0d;
                    }
                    return leftValue % rightValue;
                case "<": return leftValue < rightValue ? 1d : 0d;
                case ">": return leftValue > rightValue ? 1d : 0d;
                case "=": return leftValue == rightValue ? 1d : 0d;
                case "≤": return leftValue <= rightValue ? 1d : 0d;
                case "≥": return leftValue >= rightValue ? 1d : 0d;
                default:
                    Fail("#OP");
                    return 0d;
            }
        }

        private DataList _meanList;
        private double EvaluateVarargsFunction(string functionName, DataList args, DataDictionary cell)
        {
            double min = double.MaxValue;
            double max = double.MinValue;
            double acc = 0d;

            if (_meanList == null)
                _meanList = new DataList();
            else
                _meanList.Clear();

            for (int i = 0; i < args.Count; i++)
            {
                var arg = args[i].DataDictionary;
                if (arg["type"].String == "range")
                {
                    var rangeData = GetCellDataInRange(arg["value"].String);
                    if (rangeData == null)
                        return 0d;
                    var rangeCount = rangeData.Count;

                    for (int j = 0; j < rangeCount; j++)
                    {
                        var valueStr = rangeData[j].String;
                        if (string.IsNullOrWhiteSpace(valueStr))
                        {
                            // valid as 0
                            if (0d < min) min = 0d;
                            if (0d > max) max = 0d;
                            _meanList.Add(0d);
                        }
                        else if (!double.TryParse(valueStr, out double value))
                        {
                            Fail("#VALUE");
                            return 0d;
                        }
                        else
                        {
                            acc += value;
                            if (value < min) min = value;
                            if (value > max) max = value;
                            _meanList.Add(value);
                        }
                    }
                }
                else if (arg["type"].String == "relativerange")
                {
                    var offsetX1 = arg["offsetX"].Int;
                    var offsetY1 = arg["offsetY"].Int;
                    var offsetX2 = arg["offsetX2"].Int;
                    var offsetY2 = arg["offsetY2"].Int;

                    for (int x = offsetX1; x <= offsetX2; x++)
                    {
                        for (int y = offsetY1; y <= offsetY2; y++)
                        {
                            var valueStr = GetNodeValueRelative(x, y, cell);
                            if (valueStr == null)
                            {
                                Fail("#REFR");
                                return 0d;
                            }
                            if (string.IsNullOrWhiteSpace(valueStr))
                            {
                                // valid as 0
                                if (0d < min) min = 0d;
                                if (0d > max) max = 0d;
                                _meanList.Add(0d);
                            }
                            else if (!double.TryParse(valueStr, out double value))
                            {
                                Fail("#VALUE");
                                return 0d;
                            }
                            else
                            {
                                acc += value;
                                if (value < min) min = value;
                                if (value > max) max = value;
                                _meanList.Add(value);
                            }
                        }
                    }
                }
                else
                {
                    var value = GetDoubleValue1(arg, cell);
                    if (_evalError != null)
                        return 0d;

                    acc += value;
                    if (value < min) min = value;
                    if (value > max) max = value;
                    _meanList.Add(value);
                }
            }

            var cnt = _meanList.Count;
            switch (functionName)
            {
                case "SUM":
                    return acc;
                case "AVG":
                    if (cnt == 0)
                    {
                        Fail("#DIV/0");
                        return 0d;
                    }
                    return acc / cnt;
                case "MEAN":
                    if (cnt == 0)
                    {
                        Fail("#DIV/0");
                        return 0d;
                    }
                    _meanList.Sort();
                    return _meanList[cnt / 2].Double;
                case "COUNT":
                    return cnt;
                case "MIN":
                    if (cnt == 0)
                    {
                        Fail("#DIV/0");
                        return 0d;
                    }
                    return min;
                case "MAX":
                    if (cnt == 0)
                    {
                        Fail("#DIV/0");
                        return 0d;
                    }
                    return max;
                default:
                    Fail("#FUNC");
                    return 0d;
            }
        }

        private double EvaluateBaseMathFunction(string functionName, double arg, out bool found)
        {
            found = true;
            switch (functionName)
            {
                case "ABS": return Math.Abs(arg);
                case "NEGATE": return -arg;
                case "SQRT":
                    if (arg < 0)
                    {
                        Fail("#SQRT");
                        return 0d;
                    }
                    return Math.Sqrt(arg);
                case "SIN": return Math.Sin(arg);
                case "COS": return Math.Cos(arg);
                case "TAN": return Math.Tan(arg);
                case "ASIN":
                    if (arg < -1 || arg > 1)
                    {
                        Fail("#ASIN");
                        return 0d;
                    }
                    return Math.Asin(arg);
                case "ACOS":
                    if (arg < -1 || arg > 1)
                    {
                        Fail("#ACOS");
                        return 0d;
                    }
                    return Math.Acos(arg);
                case "ATAN": return Math.Atan(arg);
                case "EXP": return Math.Exp(arg);
                case "LN":
                    if (arg <= 0)
                    {
                        Fail("#LN");
                        return 0d;
                    }
                    return Math.Log(arg);
                case "LOG10":
                    if (arg <= 0)
                    {
                        Fail("#LOG10");
                        return 0d;
                    }
                    return Math.Log10(arg);
                case "LOG2":
                    if (arg <= 0)
                    {
                        Fail("#LOG2");
                        return 0d;
                    }
                    return Math.Log(arg, 2);
                case "ROUND": return Math.Round(arg);
                case "FLOOR": return Math.Floor(arg);
                case "CEIL": return Math.Ceiling(arg);
                default:
                    found = false;
                    return 0d;
            }
        }

        private DataList GetCellDataInRange(string range)
        {
            var paths = range.Split(':');
            if (paths.Length != 2)
            {
                Fail("#RANGE");
                return null;
            }

            DataList rangeData;
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (GetNodeValueOverride != null)
            {
                rangeData = new DataList();
                var rowStart = paths[0].ToUpper()[0] - 'A';
                var rowEnd = paths[1].ToUpper()[0] - 'A';
                var colStart = int.Parse(paths[0].Substring(1));
                var colEnd = int.Parse(paths[1].Substring(1));
                for (int row = rowStart; row <= rowEnd; row++)
                {
                    for (int col = colStart; col <= colEnd; col++)
                    {
                        var cellPath = $"{(char)(row + 'A')}{col}";
                        rangeData.Add(GetNodeValueOverride(cellPath));
                    }
                }

                if (rangeData.Count == 0)
                {
                    Fail("#RANGE");
                    return null;
                }
            }
            else
#endif
            {
                rangeData = CellHelpers.GetCellDataBetween(_cellHolder, paths[0], paths[1]);

                var rangeCount = rangeData.Count;

                if (rangeCount == 0)
                {
                    Fail("#RANGE");
                    return null;
                }

                for (int i = 0; i < rangeCount; i++)
                {
                    var cellData = rangeData[i].DataDictionary;
                    var value = cellData["value"].String;
                    rangeData[i] = value;
                }
            }

            return rangeData;
        }

        private double GetDoubleValue1(DataDictionary node, DataDictionary cell)
        {
            double value;
            if (node["type"].String == "constant")
            {
                value = node["value"].Double;
            }
            else if (node["type"].String == "cell")
            {
                var path = node["value"].String;
                var valueStr = GetNodeValue(path);
                if (valueStr == null)
                {
                    Fail("#REF1");
                    return 0d;
                }
                if (string.IsNullOrWhiteSpace(valueStr))
                {
                    // valid!
                    value = 0d;
                }
                else if (!double.TryParse(valueStr, out value))
                {
                    Fail("#VALUE");
                    return 0d;
                }
            }
            else if (node["type"].String == "relative")
            {
                var offsetX = node["offsetX"].Int;
                var offsetY = node["offsetY"].Int;
                var valueStr = GetNodeValueRelative(offsetX, offsetY, cell);
                if (valueStr == null)
                {
                    Fail("#REFR");
                    return 0d;
                }
                if (string.IsNullOrWhiteSpace(valueStr))
                {
                    // valid!
                    value = 0d;
                }
                else if (!double.TryParse(valueStr, out value))
                {
                    Fail("#VALUE");
                    return 0d;
                }
            }
            else
            {
                Fail("#TYPE"); // invalid type for operator
                return 0d;
            }
            return value;
        }

        private void Fail(string error)
        {
            _evalError = error;
        }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // for tests
        [field: NonSerialized] public Func<string, string> GetNodeValueOverride { get; set; }
#endif

        private string GetNodeValue(string path)
        {
            #if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (GetNodeValueOverride != null)
                return GetNodeValueOverride(path);
            #endif

            if (_cellHolder.CellsPath.TryGetValue(path, out var cellData))
            {
                return cellData.DataDictionary["value"].String;
            }
            else
            {
                return null;
            }
        }

        private string GetNodeValueRelative(int offsetX, int offsetY, DataDictionary localCell)
        {
            var localRow = localCell["row"].Int;
            var localCol = localCell["colNum"].Int;

            var targetRow = localRow + offsetY;
            var targetCol = localCol + offsetX;

            if (targetRow < 1 || targetCol < 1 || targetRow > 256 || targetCol > 128)
                return null;

            #if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (GetNodeValueOverride != null)
                return GetNodeValueOverride($"{(char)(targetCol - 1 + 'A')}{targetRow}");
            #endif
            
            if (_cellHolder.CellsRowCol.TryGetValue(targetRow, out var rowDict))
            {
                if (rowDict.DataDictionary.TryGetValue(targetCol, out var cellData))
                    return cellData.DataDictionary["value"].String;
            }

            return null;
        }

#endregion

#region Data Dependencies

        /*
            A1
            A2 -> A1
            A3 -> A1, A2, A2
            A4 -> A2, A3

            A4
            +-- A2
            |   +-- A1
            +-- A3
                +-- A1
                +-- A2
                    +-- A1
                +-- A2
                    +-- A1

            -> layer 1 traversal
            A4 direct deps: A2, A3
            -> full traversal (detect cycle by just checking for A4)
            A4 result: A3
        */

        [RecursiveMethod]
        private bool RefreshDependencies(DataDictionary cellData, DataDictionary executionGraph, bool skipDependentCheck = false)
        {
            if (cellData == null)
                return true;

            CellHelpers.ClearDependencies(_cellHolder, cellData);

            if (executionGraph == null)
                return true;
            
            var dependencyPaths = new DataList();

            // step 1: collect all dependencies listed in the execution graph (layer 1 traversal)
            CollectDependenciesFromGraph(dependencyPaths, cellData, executionGraph);

            // step 2: traverse the cell graph recursively, remove any dependencies > layer 1, and detect cycles (full traversal)
            var depCount = dependencyPaths.Count;
            var rootPath = cellData["path"].String;
            var fullDepList = cellData["fullDepList"].DataDictionary;
            fullDepList.Clear();
            for (int i = 0; i < depCount; i++)
            {
                var depPath = dependencyPaths[i].String;
                if (string.IsNullOrEmpty(depPath))
                {
                    // this sometimes happens, but I don't know how
                    continue;
                }

                if (!_cellHolder.CellsPath.TryGetValue(depPath, out var depCellData))
                {
                    Log($"Dependency '{depPath}' not found for cell '{rootPath}'");
                    continue;
                }

                TraverseCellsForDependencies(dependencyPaths, depCellData.DataDictionary);

                fullDepList.Add(depPath, true);
                var depDict = depCellData.DataDictionary["fullDepList"].DataDictionary;
                var depDictKeys = depDict.GetKeys();
                var depDictCount = depDictKeys.Count;
                for (int j = 0; j < depDictCount; j++)
                {
                    var transDepKey = depDictKeys[j];
                    if (!fullDepList.ContainsKey(transDepKey))
                        fullDepList.Add(transDepKey, true);
                }
            }

            /*if (dependencyPaths.Count != depCount)
            {
                Log($"Cell '{cellData["path"].String}' dropped {depCount - dependencyPaths.Count} transitive dependencies");
            }*/

            if (fullDepList.ContainsKey(rootPath))
            {
                Log($"Cell '{cellData["path"].String}' is recursively dependent on itself");
                return false;
            }

            // step 3: register dependencies in the cell holder
            depCount = dependencyPaths.Count;
            for (int i = 0; i < depCount; i++)
            {
                var depPath = dependencyPaths[i].String;
                CellHelpers.AddDependency(_cellHolder, cellData, depPath);
            }

            if (skipDependentCheck)
                return true;

            // step 4: recalculate dependents upwards
            CollectDependentsRecursively(dependencyPaths, rootPath, cellData);
            depCount = dependencyPaths.Count;
            for (int i = 0; i < depCount; i++)
            {
                var depPath = dependencyPaths[i].String;
                if (!_cellHolder.CellsPath.TryGetValue(depPath, out var depCellData))
                {
                    // shouldn't happen?
                    Log($"Dependent '{depPath}' not found for cell '{rootPath}'");
                    continue;
                }

                var depCell = depCellData.DataDictionary;
                var execGraphToken = depCell["executionGraph"];
                if (!execGraphToken.IsEmpty && execGraphToken.TokenType == TokenType.DataDictionary)
                {
                    if (!RefreshDependencies(depCell, execGraphToken.DataDictionary, skipDependentCheck: true))
                    {
                        // dependent was recursive, mark it as an error
                        Log($"Dependent '{depPath}' turned recursively dependent on itself, marking as error");
                        depCell["value"] = "#RECURS";
                        CellHelpers.ClearDependencies(_cellHolder, depCell);
                    }
                }
            }

            return true;
        }

        [RecursiveMethod]
        private void CollectDependenciesFromGraph(DataList resultPaths, DataDictionary cellData, DataDictionary executionGraph)
        {
            var type = executionGraph["type"].String;
            if (type == "cell")
            {
                var path = executionGraph["value"].String;
                resultPaths.Add(path);
            }
            else if (type == "range")
            {
                var range = executionGraph["value"].String;
                var split = range.Split(':');
                if (split.Length == 2)
                {
                    var rangeData = CellHelpers.GetCellDataBetween(_cellHolder, split[0], split[1]);
                    var rangeCount = rangeData.Count;
                    for (int i = 0; i < rangeCount; i++)
                    {
                        var cellValue = rangeData[i].DataDictionary;
                        var cellPath = cellValue["path"].String;
                        resultPaths.Add(cellPath);
                    }
                }
            }
            else if (type == "relative")
            {
                var offsetX = executionGraph["offsetX"].Int;
                var offsetY = executionGraph["offsetY"].Int;
                var localRow = cellData["row"].Int;
                var localCol = cellData["colNum"].Int;

                var targetRow = localRow + offsetY;
                var targetCol = localCol + offsetX;

                if (targetRow >= 1 && targetCol >= 1 && targetRow <= 256 && targetCol <= 128)
                {
                    var targetPath = $"{(char)(targetCol - 1 + 'A')}{targetRow}";
                    resultPaths.Add(targetPath);
                }
            }
            else if (type == "relativerange")
            {
                var offsetX = executionGraph["offsetX"].Int;
                var offsetY = executionGraph["offsetY"].Int;
                var offsetX2 = executionGraph["offsetX2"].Int;
                var offsetY2 = executionGraph["offsetY2"].Int;

                var localRow = cellData["row"].Int;
                var localCol = cellData["colNum"].Int;

                for (int xoffset = offsetX; xoffset <= offsetX2; xoffset++)
                {
                    for (int yoffset = offsetY; yoffset <= offsetY2; yoffset++)
                    {
                        var targetRow = localRow + yoffset;
                        var targetCol = localCol + xoffset;

                        if (targetRow >= 1 && targetCol >= 1 && targetRow <= 256 && targetCol <= 128)
                        {
                            var targetPath = $"{(char)(targetCol - 1 + 'A')}{targetRow}";
                            resultPaths.Add(targetPath);
                        }
                    }
                }
            }
            else if (type == "function")
            {
                var children = executionGraph["children"].DataList;
                var childCount = children.Count;
                for (int i = 0; i < childCount; i++)
                {
                    CollectDependenciesFromGraph(resultPaths, cellData, children[i].DataDictionary);
                }
            }
        }

        private void TraverseCellsForDependencies(DataList resultPaths, DataDictionary cellData)
        {
            var dependsOn = cellData["dependsOn"].DataList;
            var depCount = dependsOn.Count;

            // remove from resultPaths any transitive deps
            for (int j = 0; j < depCount; j++)
            {
                var depPath = dependsOn[j].String;
                resultPaths.Remove(depPath);
            }
        }

        [RecursiveMethod]
        private void CollectDependentsRecursively(DataList resultPaths, string rootPath, DataDictionary cellData)
        {
            var dependents = cellData["dependedBy"].DataList;
            var depCount = dependents.Count;

            for (int j = 0; j < depCount; j++)
            {
                var depPath = dependents[j].String;

                if (depPath == rootPath)
                    continue; // cycle detected, should never happen at this stage, but just in case
                if (resultPaths.Contains(depPath))
                    continue; // already collected

                resultPaths.Add(depPath);

                var depNode = _cellHolder.CellsPath[depPath].DataDictionary;
                CollectDependentsRecursively(resultPaths, rootPath, depNode);
            }
        }

#endregion

        private void Log(string message)
        {
            if (_log != null)
                _log.Log(message);
            else
                Debug.Log(message);
        }
    }
}