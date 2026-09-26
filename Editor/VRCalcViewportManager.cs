using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRC.Udon;

public class VRCalcViewportManager : EditorWindow
{
    [MenuItem("VRC VRCalc/Viewport Manager")]
    public static void ShowWindow()
    {
        GetWindow<VRCalcViewportManager>("VRCalc Viewport Manager");
    }

    private GameObject rowsRoot;
    private GameObject rowLabelsRoot;
    private GameObject columnLabelsRoot;
    private int rows = 40;
    private int columns = 19;

    private void OnGUI()
    {
        if (rowsRoot == null) rowsRoot = GameObject.Find("Canvas/Cells/ScrollView/Viewport/Rows");
        if (rowLabelsRoot == null) rowLabelsRoot = GameObject.Find("Canvas/Cells/RowLabelMask/RowLabels");
        if (columnLabelsRoot == null) columnLabelsRoot = GameObject.Find("Canvas/Cells/ColumnLabelMask/ColumnLabels");

        rowsRoot = EditorGUILayout.ObjectField("Rows Root", rowsRoot, typeof(GameObject), true) as GameObject;
        rowLabelsRoot = EditorGUILayout.ObjectField("Row Labels Root", rowLabelsRoot, typeof(GameObject), true) as GameObject;
        columnLabelsRoot = EditorGUILayout.ObjectField("Column Labels Root", columnLabelsRoot, typeof(GameObject), true) as GameObject;
        rows = EditorGUILayout.IntField("Rows", rows);
        columns = EditorGUILayout.IntField("Columns", columns);

        if (rowsRoot == null)
        {
            EditorGUILayout.HelpBox("Please select a Rows root object.", MessageType.Info);
            return;
        }

        if (GUILayout.Button("Generate!"))
        {
            Undo.RecordObject(rowsRoot, "Generate Viewport");
            CopyColumns(rowsRoot);
            CopyRows(rowsRoot);
            SetupButtons(rowsRoot);
            Undo.FlushUndoRecordObjects();
        }

        if (GUILayout.Button("Clean Up!"))
        {
            Undo.RecordObject(rowsRoot, "Clean Up Viewport");
            DestroyOtherRows(rowsRoot);
            DestroyOtherColumns(rowsRoot);
            Undo.FlushUndoRecordObjects();
        }

        if (GUILayout.Button("Copy Button Event U#"))
        {
            CopyButtonEventUdonSharp();
        }
    }

    private void CopyColumns(GameObject rowsRoot)
    {
        var example = rowsRoot.transform.Find("1/A").gameObject;
        for (int i = 1; i < columns; i++)
        {
            var newColumn = Instantiate(example, example.transform.parent, false);
            newColumn.name = ((char)('A' + i)).ToString();
        }

        var labelRoot = columnLabelsRoot;
        var labelExample = labelRoot.transform.Find("A").gameObject;
        for (int i = 1; i < columns; i++)
        {
            var newLabel = Instantiate(labelExample, labelRoot.transform, false);
            newLabel.name = ((char)('A' + i)).ToString();
            newLabel.GetComponentInChildren<TextMeshProUGUI>().text = ((char)('A' + i)).ToString();
        }
    }

    private void CopyRows(GameObject rowsRoot)
    {
        var example = rowsRoot.transform.Find("1").gameObject;
        for (int i = 1; i < rows; i++)
        {
            var rowTransform = Instantiate(example, rowsRoot.transform, false);
            rowTransform.name = (i + 1).ToString();
        }

        var labelRoot = rowLabelsRoot;
        var labelExample = labelRoot.transform.Find("1").gameObject;
        for (int i = 1; i < rows; i++)
        {
            var newLabel = Instantiate(labelExample, labelRoot.transform, false);
            newLabel.name = (i + 1).ToString();
            newLabel.GetComponentInChildren<TextMeshProUGUI>().text = (i + 1).ToString();
        }
    }

    private void DestroyOtherColumns(GameObject rowsRoot)
    {
        // destroy all columns higher than A in the 1 row
        var rowTransform = rowsRoot.transform.Find("1");
        if (rowTransform == null)
            return;
        
        for (int i = 1; i < columns + 1; i++)
        {
            var labelTransform = rowTransform.transform.Find(((char)('A' + i)).ToString());
            if (labelTransform == null)
                continue;

            DestroyImmediate(labelTransform.gameObject);
        }

        var labelRoot = columnLabelsRoot;
        if (labelRoot == null)
            return;

        for (int i = 1; i < columns + 1; i++)
        {
            var labelTransform = labelRoot.transform.Find(((char)('A' + i)).ToString());
            if (labelTransform == null)
                continue;

            DestroyImmediate(labelTransform.gameObject);
        }
    }

    private void DestroyOtherRows(GameObject rowsRoot)
    {
        // iterate higher numbers and destroy the label in each row if it exists
        for (int i = 2; i <= rows; i++)
        {
            var rowTransform = rowsRoot.transform.Find(i.ToString());
            if (rowTransform == null)
                continue;

            DestroyImmediate(rowTransform.gameObject);
        }

        var labelRoot = rowLabelsRoot;
        if (labelRoot == null)
            return;

        for (int i = 2; i <= rows; i++)
        {
            var labelTransform = labelRoot.transform.Find(i.ToString());
            if (labelTransform == null)
                continue;

            DestroyImmediate(labelTransform.gameObject);
        }
    }

    private void SetupButtons(GameObject rowsRoot)
    {
        for (int i = 1; i <= rows; i++)
        {
            var rowTransform = rowsRoot.transform.Find(i.ToString());
            if (rowTransform == null)
                continue;

            for (int j = 0; j < columns; j++)
            {
                var columnLetter = ((char)('A' + j)).ToString();
                var fieldTransform = rowTransform.transform.Find(columnLetter);
                if (fieldTransform == null)
                    continue;

                var button = fieldTransform.GetComponentInChildren<Button>();
                if (button == null)
                    continue;
                
                ChangeExistingStringParam(button, 0, $"{columnLetter}{i}");

                var trigger = fieldTransform.GetComponentInChildren<EventTrigger>();
                if (trigger == null)
                    continue;

                ChangeExistingStringParam(trigger, 0, $"{columnLetter}{i}");
            }
        }

        for (int i = 1; i <= rows; i++)
        {
            var rowLabelTransform = rowLabelsRoot.transform.Find(i.ToString());
            if (rowLabelTransform == null)
                continue;
            
            var button = rowLabelTransform.GetComponentInChildren<Button>();
            if (button == null)
                continue;
            
            ChangeExistingStringParam(button, 0, $"Row_{i}");
        }

        for (int j = 0; j < columns; j++)
        {
            var columnLetter = ((char)('A' + j)).ToString();
            var columnLabelTransform = columnLabelsRoot.transform.Find(columnLetter);
            if (columnLabelTransform == null)
                continue;
            
            var button = columnLabelTransform.GetComponentInChildren<Button>();
            if (button == null)
                continue;
            
            ChangeExistingStringParam(button, 0, $"Col_{columnLetter}");
        }
    }

    private static void ChangeExistingStringParam(Button button, int listenerIndex, string newValue)
    {
        /*
        m_OnClick:
            m_PersistentCalls:
                m_Calls:
                - m_Target: {fileID: 1862981598}
                    m_TargetAssemblyTypeName: 
                    m_MethodName: SendCustomEvent
                    m_Mode: 5
                    m_Arguments:
                        m_ObjectArgument: {fileID: 0}
                        m_ObjectArgumentAssemblyTypeName: UnityEngine.Object, UnityEngine
                        m_IntArgument: 0
                        m_FloatArgument: 0
                        m_StringArgument: A1
                        m_BoolArgument: 0
                    m_CallState: 2
        */

        var so = new SerializedObject(button);
        var callsProp = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");

        if (listenerIndex < callsProp.arraySize)
        {
            var call = callsProp.GetArrayElementAtIndex(listenerIndex);
            var stringArg = call.FindPropertyRelative("m_Arguments.m_StringArgument");

            stringArg.stringValue = newValue;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(button);
        }
        else
        {
            Debug.LogError("Listener index out of range.");
        }
    }

    private static void ChangeExistingStringParam(EventTrigger trigger, int listenerIndex, string newValue)
    {
        /*
        m_Delegates:
        - eventID: 13
            callback:
            m_PersistentCalls:
                m_Calls:
                - m_Target: {fileID: 2838869365602120768}
                m_TargetAssemblyTypeName: 
                m_MethodName: SendCustomEvent
                m_Mode: 5
                m_Arguments:
                    m_ObjectArgument: {fileID: 0}
                    m_ObjectArgumentAssemblyTypeName: UnityEngine.Object, UnityEngine
                    m_IntArgument: 0
                    m_FloatArgument: 0
                    m_StringArgument: BeginDragCell
                    m_BoolArgument: 0
                m_CallState: 2
        - eventID: 14
            callback:
            m_PersistentCalls:
                m_Calls:
                - m_Target: {fileID: 2838869365602120768}
                m_TargetAssemblyTypeName: 
                m_MethodName: SendCustomEvent
                m_Mode: 5
                m_Arguments:
                    m_ObjectArgument: {fileID: 0}
                    m_ObjectArgumentAssemblyTypeName: UnityEngine.Object, UnityEngine
                    m_IntArgument: 0
                    m_FloatArgument: 0
                    m_StringArgument: EndDragCell
                    m_BoolArgument: 0
                m_CallState: 2
        */

        var so = new SerializedObject(trigger);
        var delegatesProp = so.FindProperty("m_Delegates");

        var type = 0;
        foreach (SerializedProperty delegateProp in delegatesProp)
        {
            var callbackProp = delegateProp.FindPropertyRelative("callback");
            var callsProp = callbackProp.FindPropertyRelative("m_PersistentCalls.m_Calls");

            if (listenerIndex < callsProp.arraySize)
            {
                var call = callsProp.GetArrayElementAtIndex(listenerIndex);
                var stringArg = call.FindPropertyRelative("m_Arguments.m_StringArgument");

                stringArg.stringValue = (type == 0 ? "BeginDragCell" : (type == 1 ? "EndDragCell" : "PointerEnter")) + newValue; // cursed, but w/e
                so.ApplyModifiedProperties();

                EditorUtility.SetDirty(trigger);
            }
            else
            {
                Debug.LogError("Listener index out of range.");
            }

            type++;
        }
    }

    private void CopyButtonEventUdonSharp()
    {
        var sb = new StringBuilder();
        for (int i = 1; i <= rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                var columnLetter = ((char)('A' + j)).ToString();
                sb.AppendLine($"        public void {columnLetter}{i}() => Handle(\"{columnLetter}{i}\");");
                sb.AppendLine($"        public void BeginDragCell{columnLetter}{i}() => BeginDragCell(\"{columnLetter}{i}\");");
                sb.AppendLine($"        public void EndDragCell{columnLetter}{i}() => EndDragCell(\"{columnLetter}{i}\");");
                sb.AppendLine($"        public void PointerEnter{columnLetter}{i}() => PointerEnter(\"{columnLetter}{i}\");");
            }
        }

        sb.AppendLine();

        for (int i = 1; i <= rows; i++)
        {
            sb.AppendLine($"        public void Row_{i}() => HandleRow({i});");
        }

        sb.AppendLine();

        for (int j = 0; j < columns; j++)
        {
            var columnLetter = ((char)('A' + j)).ToString();
            sb.AppendLine($"        public void Col_{columnLetter}() => HandleCol(\"{columnLetter}\");");
        }

        Debug.Log(sb.ToString());
        EditorGUIUtility.systemCopyBuffer = sb.ToString();
    }
}