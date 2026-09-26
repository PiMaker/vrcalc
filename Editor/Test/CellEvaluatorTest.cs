#if UNITY_EDITOR && !COMPILER_UDONSHARP

using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Data;

namespace pi.vrcalc.Test
{
    public class CellEvaluatorTest
    {
        private CellEvaluator _cellEvaluator;
        private DataDictionary _cellData;

        private static readonly string[,] TestGrid = new string[,]
        {
            // A    B    C    D
            { "0", "0", "0", "" }, // 1
            { "1", "2", "3", "4" }, // 2
            { "0", "0", "0", "text" }, // 3
            { "9.0", "9.5", "10.0", "10.5" }, // 4
            { "0", "0", "0", "0" }, // 5
            { "1", "2", "3", "4" }, // 6
            { "1", "2", "3", "4" }, // 7
            { "1", "2", "3", "4" }, // 8
            { "1", "2", "3", "4" }, // 9
            { "1", "2", "3", "4" }, // 10
            { "1", "2", "3", "4" }, // 11
            { "1", "2", "3", "4" }, // 12
        };

        [SetUp]
        public void SetUpFn()
        {
            var go = new GameObject("CellEvaluatorTest");
            _cellEvaluator = go.AddComponent<CellEvaluator>();
            _cellEvaluator.InitOperators();
            _cellEvaluator.GetNodeValueOverride = (path) =>
            {
                // path == "A1" => row 0, col 0
                var col = path[0] - 'A';
                var row = int.Parse(path.Substring(1)) - 1;
                if (row < 0 || row >= TestGrid.GetLength(0) || col < 0 || col >= TestGrid.GetLength(1))
                    return null;
                return TestGrid[row, col];
            };

            // only the required stuff
            _cellData = new DataDictionary();
            _cellData["row"] = 2;
            _cellData["col"] = "B";
            _cellData["colNum"] = 2;
            _cellData["path"] = "B2";
        }

        [TearDown]
        public void TearDownFn()
        {
            Object.DestroyImmediate(_cellEvaluator.gameObject);
            _cellEvaluator = null;
        }

        [Test]
        public void TestEvaluateSimpleExpression()
        {
            var graph = _cellEvaluator.ParseFormula("=1 + 2 * 3");
            var str = CellEvaluator.DebugPrintGraph(graph);
            Debug.Log($"Graph: {str}");
            Debug.Log($"Last Error: {_cellEvaluator.ParseError()}");
            var result = _cellEvaluator.EvaluateGraph(graph, _cellData);
            Debug.Log($"Result: {result}");

            Assert.AreEqual(str.Replace("\r", ""), @"Node(function, '+', children:
  Node(constant, '1')
  Node(function, '*', children:
    Node(constant, '2')
    Node(constant, '3')
  )
)".Replace("\r", ""));
            Assert.AreEqual("7", result);
            Assert.AreEqual(null, _cellEvaluator.ParseError());
        }

        [Test]
        // basic arithmetic, both argument orders
        [TestCase("=1+1", "2")]
        [TestCase("=2+3", "5")]
        [TestCase("=3+2", "5")]
        [TestCase("=5-2", "3")]
        [TestCase("=2-5", "-3")]
        [TestCase("=2*3", "6")]
        [TestCase("=3*2", "6")]
        [TestCase("=6/2", "3")]
        [TestCase("=6/4", "1.5")]
        [TestCase("=7%3", "1")]
        [TestCase("=7%4", "3")]
        // chaining / left-to-right
        [TestCase("=1+1+1+1+1+1    + 1+ 1", "8")]
        [TestCase("=2+3+4", "9")]
        [TestCase("=10-3-2", "5")]
        [TestCase("=2 * 3 * 4", "24")]
        [TestCase("=100/4/5", "5")]
        [TestCase("=2+3-4+5", "6")]
        // precedence
        [TestCase("=2+3*4", "14")]
        [TestCase("=2*3+4", "10")]
        [TestCase("=10-2*3", "4")]
        [TestCase("=2*3-10", "-4")]
        [TestCase("=2+6/2", "5")]
        [TestCase("=6/2+2", "5")]
        [TestCase("=2+8%3", "4")]
        [TestCase("=8%3+2", "4")]
        // parentheses / nesting
        [TestCase("=(1+1)*2", "4")]
        [TestCase("=((1+1))*2", "4")]
        [TestCase("=(((1+1))*(2))+2", "6")]
        [TestCase("=(2+3)*4", "20")]
        [TestCase("=((2))", "2")]
        [TestCase("=((1+2))", "3")]
        [TestCase("=(10-2)*(3+1)", "32")]
        // special values: zero, negatives, decimals
        [TestCase("=0+0", "0")]
        [TestCase("=1-1", "0")]
        [TestCase("=5-10", "-5")]
        [TestCase("=0*999", "0")]
        [TestCase("=999*0", "0")]
        [TestCase("=0/5", "0")]
        [TestCase("=1.5+2.5", "4")]
        [TestCase("=1.5*2", "3")]
        [TestCase("=3/2", "1.5")]
        [TestCase("=56-22.5", "33.5")]
        [TestCase("=22.5-56", "-33.5")]
        [TestCase("=0.1+0.2", "0.3")]
        // whitespace handling
        [TestCase("=   1   +   2  ", "3")]
        [TestCase("=  2  *  (  3  +  4  )  ", "14")]
        // cell references, both argument orders
        [TestCase("=A2+B2", "3")]
        [TestCase("=B2+A2", "3")]
        [TestCase("=C2*B2+8", "14")]
        [TestCase("=D2*B2+8/2", "12")]
        [TestCase("=A2", "1")]
        [TestCase("=B2", "2")]
        [TestCase("=C2", "3")]
        [TestCase("=D2", "4")]
        [TestCase("=A2*B2*C2*D2", "24")]
        [TestCase("=A2+B2+C2+D2", "10")]
        [TestCase("=D2-A2", "3")]
        [TestCase("=C2/B2", "1.5")]
        [TestCase("=B2*B2", "4")]
        [TestCase("=D12", "4")]
        [TestCase("=D12*C12-5", "7")]
        // empty cells behave as 0
        [TestCase("=D1+D1+D1+D1", "0")]
        [TestCase("=D1+D1+D1+D1+1", "1")]
        [TestCase("=D1+5", "5")]
        [TestCase("=A1+B1+C1", "0")]
        // out-of-range bare refs surface from eval
        [TestCase("=A99", "#REF1")]
        [TestCase("=S99", "#REF1")]
        [TestCase("=A0", "#REF1")]
        [TestCase("=A13", "#REF1")]
        [TestCase("=E1", "#REF1")]
        // unary minus / sign handling
        [TestCase("=-1", "-1")]
        [TestCase("=-1+2", "1")]
        [TestCase("=2+-1", "1")]
        [TestCase("=2*-3", "-6")]
        [TestCase("=-(1+2)", "-3")]
        [TestCase("=(-1)*2", "-2")]
        [TestCase("=-(2+3)*4", "-20")]
        [TestCase("=-ID(2+3)*4", "-20")]
        [TestCase("=-ID(-ID(2+3))*-4.5", "-22.5")]
        // unary plus / double operators
        [TestCase("=+1", "1")]
        [TestCase("=1++2", "3")]
        [TestCase("=1--2", "3")]
        [TestCase("=1+-2", "-1")]
        [TestCase("=1*-2", "-2")]
        [TestCase("=+1+2", "3")]
        // leading/trailing dot on numbers
        [TestCase("=.5", "0.5")]
        [TestCase("=.5+1", "1.5")]
        [TestCase("=1.", "1")]
        [TestCase("=1.+2", "3")]
        // scientific notation
        [TestCase("=1E5", "100000")]
        [TestCase("=1e5", "100000")]
        [TestCase("=1.5E3", "1500")]
        [TestCase("=1.5e3", "1500")]
        [TestCase("=1E-5", "1E-05")]
        [TestCase("=1.5E-3", "0.0015")]
        [TestCase("=1E20", "1E+20")]
        // exponentiation
        [TestCase("=2^3", "8")]
        [TestCase("=2^10", "1024")]
        [TestCase("=2^3^2", "512")]
        [TestCase("=4*2^8", "1024")]
        [TestCase("=2^8*4", "1024")]
        [TestCase("=4+2^8", "260")]
        // case insensitivity: formula is normalised to uppercase
        [TestCase("=a2+b2", "3")]
        [TestCase("=A2*b2", "2")]
        [TestCase("=id(5)", "5")]
        [TestCase("=Id(5)", "5")]
        // ID function forms
        [TestCase("=ID(5)", "5")]
        [TestCase("=ID(A2)", "1")]
        [TestCase("=ID(1+2)", "3")]
        [TestCase("=ID(ID(1))", "1")]
        [TestCase("=ID(1)+2", "3")]
        [TestCase("=1+ID(2)", "3")]
        [TestCase("=ID(2)*3", "6")]
        [TestCase("=ID( 1 )", "1")]
        // Variadic functions
        [TestCase("=SUM(1, 2, 3)", "6")]
        [TestCase("=SUM(A2, B2, C2, D2)", "10")]
        [TestCase("=SUM(A2:D2)", "10")]
        [TestCase("=SUM(A10:B12 , C10:D12, 5.0)", "35")]
        [TestCase("=SUM(A10:B12, C10:D12)", "30")]
        [TestCase("=AVG(A10:B12, C10:D12)", "2.5")]
        [TestCase("=MAX(A10:B12, C10:D12)", "4")]
        [TestCase("=MIN(A10:B12, C10:D12)", "1")]
        [TestCase("=COUNT(ID(A10:B12), ID(ID(C10:D12)))", "12")]
        [TestCase("=MEAN(A10:B12, C10:D12)", "3")]
        [TestCase("=MEAN(1, 1, 1, 1, 1, 1, 1, 99999)", "1")]
        [TestCase("=COUNT(d1)", "1")]
        [TestCase("=COUNT(D1:D1)", "1")]
        // basic math functions
        [TestCase("=ABS(-5)", "5")]
        [TestCase("=ROUND(SIN(PI()))", "0")]
        [TestCase("=EXP(1)-E()", "0")]
        // Relative addressing (at B2)
        [TestCase("=X1", "3")]
        [TestCase("=ID(Y2+X-1)", "10.5")]
        [TestCase("=ID(Y2+(X-1))", "10.5")]
        [TestCase("=X-1", "1")]
        [TestCase("=X1Y2", "10")]
        [TestCase("=Y2X1", "10")]
        [TestCase("=Y2+.5", "10")]
        [TestCase("=X-8", "#REFR")]
        [TestCase("=Y80", "#REFR")]
        [TestCase("=X-2Y-2", "#REFR")]
        [TestCase("=SUM(X-1:X2)", "10")]
        [TestCase("=SUM(X-1  :   X2)", "10")]
        [TestCase("=SUM(X-1Y0:X2Y-1)", "10")]
        // Conditional IF
        [TestCase("=IF(1, 10, 20)", "10")]
        [TestCase("=IF(0, 10, 20)", "20")]
        [TestCase("=IF(15-10-5, B2, B4)", "9.5")]
        [TestCase("=IF(SIN(PI()/2), B2, X-100)", "2")] // X-100 not evaluated!
        [TestCase("=IF(TAN(PI()/2), X-100, B2)", "#REFR")]
        // Comparison operators
        [TestCase("=1<2", "1")]
        [TestCase("=2<1", "0")]
        [TestCase("=1>2", "0")]
        [TestCase("=2>1", "1")]
        [TestCase("=1=1", "1")]
        [TestCase("=1=2", "0")]
        [TestCase("=1<=2", "1")]
        [TestCase("=2<=1", "0")]
        [TestCase("=4>= 4", "1")]
        [TestCase("=4  <= 4", "1")]
        [TestCase("= SUM(IF(2*4 <=2+ID(3+3 ), A2:D2, A1:D1))", "10")]
        public void TestMathExpressions(string formula, string expectedResult)
        {
            var graph = _cellEvaluator.ParseFormula(formula);
            var result = _cellEvaluator.EvaluateGraph(graph, _cellData);
            Assert.AreEqual(expectedResult, result);
            Assert.AreEqual(null, _cellEvaluator.ParseError());
        }

        [Test]
        [TestCase("=", "Formula is empty after normalization")]
        [TestCase("=    ", "Formula is empty after normalization")]
        [TestCase("", "Formula is empty or whitespace")]
        [TestCase("   ", "Formula is empty or whitespace")]
        [TestCase("=1+", "Unexpected end of formula")]
        [TestCase("=1+*2", "Unexpected character '*' in formula")]
        [TestCase("=*2", "Unexpected character '*' in formula")]
        [TestCase("=)", "Unexpected character ')' in formula")]
        [TestCase("=()", "Empty parantheses are not allowed")]
        [TestCase("=1)", "Unexpected characters at end of formula: ')'")]
        [TestCase("=1+2 3", "Invalid character after constant: '3'")]
        [TestCase("=UNCLOSED(", "Expected ')' to close function arguments")]
        [TestCase("=UNCLOSED(A1, 17", "Expected ')' to close function arguments")]
        [TestCase("=UNCLOSED(A1, 17,   ", "Expected ')' to close function arguments")]
        [TestCase("=UNCLOSED(1,2   ", "Expected ')' to close function arguments")]
        [TestCase("=(1+2", "Unexpected end of formula. Missing ')'?")]
        [TestCase("=(1+2(", "Invalid character after constant: '('")]
        [TestCase("=1,2", "Unexpected characters at end of formula: ',2'")]
        [TestCase("=1(,2", "Invalid character after constant: '('")]
        // malformed numbers
        [TestCase("=1..5", "Failed to parse constant '1..5' at end of formula")]
        [TestCase("=1.2.3", "Failed to parse constant '1.2.3' at end of formula")]
        [TestCase("=1..", "Failed to parse constant '1..' at end of formula")]
        [TestCase("=1..5+2", "Failed to parse constant '1..5'")]
        // scientific notation 'E' is not in the digit loop, stops at E
        [TestCase("=1.5E", "Failed to parse constant '1.5E' at end of formula")]
        [TestCase("=1.5E5.5", "Invalid character after constant: '.'")]
        [TestCase("=1.5X", "Invalid character after constant: 'X'")]
        // leading dot alone
        [TestCase("=.", "Failed to parse constant '.' at end of formula")]
        // relative addressinng
        [TestCase("=X0Y0", "Relative address cannot refer to itself")]
        [TestCase("=X0", "Relative address cannot refer to itself")]
        [TestCase("=Y0", "Relative address cannot refer to itself")]
        [TestCase("=X-2X4", "Relative offset cannot have the same axis twice")]
        [TestCase("=Y-2Y4", "Relative offset cannot have the same axis twice")]
        [TestCase("=X9999999999999999999999999", "Failed to parse relative offset '9999999999999999999999999'")]
        public void TestParseError(string formula, string expectedError)
        {
            var graph = _cellEvaluator.ParseFormula(formula);
            Assert.AreEqual(expectedError, _cellEvaluator.ParseError());
        }

        [Test]
        [TestCase("=UNKNOWNFUNC(3, 5)", "#FUNC")]
        [TestCase("=1/0", "#DIV/0")]
        [TestCase("=5/0", "#DIV/0")]
        [TestCase("=A2/0", "#DIV/0")]
        [TestCase("=1%0", "#MOD/0")]
        [TestCase("=5%0", "#MOD/0")]
        [TestCase("=A2%0", "#MOD/0")]
        [TestCase("=1+D3", "#VALUE")] // D3 is "text"
        [TestCase("=D3+1", "#VALUE")]
        [TestCase("=D3*2", "#VALUE")]
        [TestCase("=2-D3", "#VALUE")]
        [TestCase("=UNKNOWNFUNC()", "#FUNC")]
        [TestCase("=UNKNOWNFUNC(1)", "#FUNC")]
        // arity errors
        [TestCase("=ID()", "#ARGS")]
        [TestCase("=ID(1, 2)", "#ARGS")]
        [TestCase("=ID(1, 2, 3)", "#ARGS")]
        [TestCase("=IF(1, 2, 3, 4)", "#IFARGS")]
        [TestCase("=IF(B2, ID(B2+1))", "#IFARGS")]
        public void TestEvalError(string formula, string expectedError)
        {
            var graph = _cellEvaluator.ParseFormula(formula);
            var result = _cellEvaluator.EvaluateGraph(graph, _cellData);
            Assert.AreEqual(null, _cellEvaluator.ParseError());
            Assert.AreEqual(expectedError, result);
        }
    }
}
#endif