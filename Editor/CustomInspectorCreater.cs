using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Yorozu.EditorTool.CustomInspector
{
	public class CustomInspectorCreateEditor : EditorWindow
	{
		[MenuItem("Tools/CreateCustomInspectorTool")]
		private static void ShowWindow()
		{
			var window = GetWindow<CustomInspectorCreateEditor>();
			window.titleContent = new GUIContent("CreateCustomInspector");
			window.Show();
		}

		private MonoScript _currentScript;
		private FieldInfo[] _fieldInfos;
		private bool[] _checks;
		private bool _requireTarget;

		private void OnEnable()
		{
			SetFieldInfo();
		}

		private void OnGUI()
		{
			using (var change = new EditorGUI.ChangeCheckScope())
			{
				_currentScript =
					(MonoScript) EditorGUILayout.ObjectField("Script", _currentScript, typeof(MonoScript), false);
				if (change.changed)
				{
					SetFieldInfo();
				}
			}

			_requireTarget = EditorGUILayout.Toggle("Set Script Object", _requireTarget);

			if (_fieldInfos != null)
			{
				EditorGUILayout.LabelField("Create Field Property", EditorStyles.boldLabel);
				for (var i = 0; i < _fieldInfos.Length; i++)
				{
					using (new GUILayout.HorizontalScope())
					{
						_checks[i] = EditorGUILayout.ToggleLeft(GUIContent.none, _checks[i]);
						EditorGUILayout.LabelField(_fieldInfos[i].Name);
					}
				}
			}

			using (new EditorGUI.DisabledScope(_currentScript == null))
			{
				if (GUILayout.Button("Create Script"))
				{
					CreateScript();
				}
			}
		}

		private void SetFieldInfo()
		{
			if (_currentScript == null)
				return;

			var type = _currentScript.GetClass();

			_fieldInfos = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
					.Where(f => f.IsSerializable())
					.ToArray()
				;
			_checks = new bool[_fieldInfos.Length];
			for (var i = 0; i < _checks.Length; i++)
				_checks[i] = true;
		}

		private void CreateScript()
		{
			var scriptPath = Path.GetDirectoryName(AssetDatabase.GetAssetPath(_currentScript));
			var saveName = _currentScript.GetClass().Name + "Editor";
			var savePath = EditorUtility.SaveFilePanelInProject("Select Save Script Path", saveName, "cs", "Choice", scriptPath);

			if (string.IsNullOrEmpty(savePath))
				return;

			// すでにあった場合は最後に追加する
			if (!File.Exists(savePath))
			{
				File.WriteAllText(savePath, GetScript());
				AssetDatabase.Refresh();

				return;
			}

			var text = File.ReadAllText(savePath);

			var splits = text.Split('\n');

			// UnityEditor をusing してなかったら追加
			if (!splits.Any(l => l.StartsWith("using UnityEditor")))
			{
				for (int i = 0; i < splits.Length; i++)
				{
					if (splits[i].StartsWith("namespace") ||
					    splits[i].StartsWith("public") ||
					    splits[i].StartsWith("internal"))
					{
						splits[i] = "#if UNITY_EDITOR\nusing UnityEditor;\n#endif\n" + splits[i];

						break;
					}
				}
			}

			File.WriteAllText(savePath, string.Join("\n", splits) + "\n" + GetScript());
			AssetDatabase.Refresh();
		}

		/// <summary>
		/// スクリプトを生成
		/// </summary>
		private string GetScript()
		{
			var builder = new StringBuilder();
			var indent = 0;
			Action<string> action = str =>
			{
				for (var i = 0; i < indent; i++)
					builder.Append('\t');

				builder.AppendLine(str);
			};

			builder.AppendLine("using UnityEditor;");
			var scriptClass = _currentScript.GetClass();
			action.Invoke("");
			if (!string.IsNullOrEmpty(scriptClass.Namespace))
			{
				action.Invoke("namespace " + scriptClass.Namespace);
				action.Invoke("{");
				indent++;
			}
			action.Invoke("[CustomEditor(typeof(" + scriptClass.Name + "))]");
			action.Invoke("public class " + scriptClass.Name + "Editor : Editor");
			action.Invoke("{");

			{
				indent++;

				if (_requireTarget)
				{
					action.Invoke("private " + scriptClass.Name + " _" +
					              ToTopLower(scriptClass.Name) + ";");
					action.Invoke("");
				}

				if (_checks.Count(b => b) > 0)
				{
					for (var i = 0; i < _checks.Length; i++)
					{
						if (!_checks[i])
							continue;

						action.Invoke("private SerializedProperty " + _fieldInfos[i].Name + ";");
					}
				}

				action.Invoke("private SerializedProperty _script;");

				action.Invoke("");
				action.Invoke("private void OnEnable()");
				action.Invoke("{");
				{
					indent++;
					action.Invoke("_script = serializedObject.FindProperty(\"m_Script\");");
					if (_checks.Count(b => b) > 0)
					{
						for (var i = 0; i < _checks.Length; i++)
						{
							if (!_checks[i])
								continue;

							action.Invoke(_fieldInfos[i].Name + " = serializedObject.FindProperty(\"" +
							              _fieldInfos[i].Name + "\");");
						}
					}

					if (_requireTarget)
					{
						action.Invoke("_" + ToTopLower(scriptClass.Name) + " = target as " + scriptClass.Name + ";");
					}

					indent--;
				}

				action.Invoke("}");
				action.Invoke("");

				action.Invoke("public override void OnInspectorGUI()");
				action.Invoke("{");
				{
					indent++;
					action.Invoke("using (new EditorGUI.DisabledScope(true))");
					action.Invoke("{");
					{
						indent++;
						action.Invoke("EditorGUILayout.PropertyField(_script);");
						indent--;
					}
					action.Invoke("}");

					if (_checks.Count(b => b) > 0)
					{
						for (var i = 0; i < _checks.Length; i++)
						{
							if (!_checks[i])
								continue;

							if (_fieldInfos[i].FieldType.GetArrayType() == null)
							{
								action.Invoke("EditorGUILayout.PropertyField(" + _fieldInfos[i].Name + ");");
							}
							else
							{
								action.Invoke("EditorGUILayout.PropertyField(" + _fieldInfos[i].Name + ", true);");
							}
						}
					}
					else
					{
						action.Invoke("base.OnInspectorGUI();");
					}

					indent--;
				}

				action.Invoke("}");

				indent--;
			}

			if (!string.IsNullOrEmpty(scriptClass.Namespace))
			{
				action.Invoke("}");
				indent--;
			}
			action.Invoke("}");

			return builder.ToString();
		}

		private static string ToTopLower(string value)
		{
			if (value.Length <= 0)
				return string.Empty;

			return char.ToLower(value[0]) + value.Substring(1);
		}
	}

	internal static class FieldInfoExtensions
	{
		internal static bool IsSerializable(this FieldInfo fieldInfo)
		{
			var attributes = fieldInfo.GetCustomAttributes(true);

			if (attributes.Any(attr => attr is NonSerializedAttribute))
				return false;

			if (fieldInfo.IsPrivate && !attributes.Any(attr => attr is SerializeField))
				return false;

			return fieldInfo.FieldType.IsSerializable();
		}

		private static bool IsSerializable(this Type type)
		{
			if (type.IsSubclassOf(typeof(UnityEngine.Object)) ||
			    type.IsEnum ||
			    type.IsValueType ||
			    type == typeof(string)
			)
				return true;

			var arrayType = type.GetArrayType();
			if (arrayType != null)
				return arrayType.IsSerializable();

			return false;
		}

		internal static Type GetArrayType(this Type type)
		{
			if (type.IsArray)
				return type.GetElementType();

			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
				return type.GetGenericArguments()[0];

			return null;
		}
	}
}
