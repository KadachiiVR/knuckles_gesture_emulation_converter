using System.Collections;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Animations;
using UnityEditor.Animations;
using UnityEditor.IMGUI.Controls;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
//using VRLabs.AV3Manager;
using ValueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType;

namespace Kadachii.GestureEmulationConverter
{
    // TODO: Make it add to avatar parameters and menu
    public class GestureEmulationConverter : EditorWindow
    {
        public VRCAvatarDescriptor avatar;
        public AnimatorController fxAnimator;
        public VRCExpressionParameters expressionParameters;
        public VRCExpressionsMenu expressionsMenu;
        public string paramSuffix = "_Emu";
        public bool defaultEmulatorModeOn = true;
        public string toggleParam = "GestureEmulation";
/*        private (string paramName, AnimatorControllerParameterType type)[] paramsToConvert = new (string paramName, AnimatorControllerParameterType type)[]
        {
            ("GestureLeft", AnimatorControllerParameterType.Int),
            ("GestureLeftWeight", AnimatorControllerParameterType.Float),
            ("GestureRight", AnimatorControllerParameterType.Int),
            ("GestureRightWeight", AnimatorControllerParameterType.Float)
        };*/

        private Dictionary<string, AnimatorControllerParameterType> paramsToConvert = new Dictionary<string, AnimatorControllerParameterType>
        {
            { "GestureLeft", AnimatorControllerParameterType.Int },
            { "GestureLeftWeight", AnimatorControllerParameterType.Float },
            { "GestureRight", AnimatorControllerParameterType.Int },
            { "GestureRightWeight", AnimatorControllerParameterType.Float }
        };

        private readonly Dictionary<AnimatorControllerParameterType, ValueType> paramTypeLookup = new Dictionary<AnimatorControllerParameterType, ValueType>
        {
            { AnimatorControllerParameterType.Bool, ValueType.Bool },
            { AnimatorControllerParameterType.Float, ValueType.Float },
            { AnimatorControllerParameterType.Int, ValueType.Int }
        };

        private const string GENERATED_ASSET_DIRECTORY = "Assets/Kadachii/KnucklesGestureEmulationConverter/GeneratedAssets";

        private Dictionary<string, AnimatorControllerParameterType> newParamsToAdd = new Dictionary<string, AnimatorControllerParameterType>();
        private bool wdOn = false;
        private List<AnimatorLayerInfo> layerSelections = new List<AnimatorLayerInfo>();
        private MultiColumnHeader colHeader;
        private MultiColumnHeaderState.Column[] headerCols;
        private Texture2D icon;

        [MenuItem("Tools/Kadachii/GestureEmulation/Converter")]
        static void Init()
        {
            GestureEmulationConverter window = (GestureEmulationConverter)EditorWindow.GetWindow(typeof(GestureEmulationConverter));
            window.titleContent = new GUIContent("Gesture Emulation Converter");
            window.Show();
        }

        private void OnEnable()
        {
            headerCols = new MultiColumnHeaderState.Column[] {
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent { text = "#" },
                    width = 15,
                    canSort = false,
                    autoResize = false
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent { text = "Layer Name" },
                    canSort = false,
                    autoResize = true
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent { text = "Ratio" },
                    canSort = false,
                    autoResize = true
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent { text = "Convert?" },
                    width = 65,
                    canSort = false,
                    autoResize = false
                }
            };
            colHeader = new MultiColumnHeader(new MultiColumnHeaderState(headerCols));
            colHeader.height = 20;
            colHeader.ResizeToFit();

            icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Kadachii/KnucklesGestureEmulationConverter/icon.png");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("<size=20><color=cyan>Gesture Emulation Converter</color></size> v0.1", new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleCenter });
            EditorGUILayout.Space();

            if (!AV3ManagerFunctionWrapper.CheckForAV3ManagerFunctions())
            {
                //EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField("Error: Missing dependency (VRLabs Avatars 3.0 Manager)");
                if (GUILayout.Button("Download the latest release"))
                {
                    Application.OpenURL("https://github.com/VRLabs/Avatars-3.0-Manager/releases/latest");
                }

                //EditorGUILayout.EndHorizontal();

                return;
            }

            EditorGUILayout.BeginHorizontal();
            avatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("Avatar", avatar, typeof(VRCAvatarDescriptor), true);
/*            if (GUILayout.Button("Auto-Fill"))
            {
                PopulateFieldsFromAvatar();
            }*/
            EditorGUILayout.EndHorizontal();

/*            fxAnimator = (AnimatorController)EditorGUILayout.ObjectField("FX Animator", fxAnimator, typeof(AnimatorController), true);
            expressionParameters = (VRCExpressionParameters)EditorGUILayout.ObjectField("Expression Parameters", expressionParameters, typeof(VRCExpressionParameters), true);
            expressionsMenu = (VRCExpressionsMenu)EditorGUILayout.ObjectField("Expressions Menu", expressionsMenu, typeof(VRCExpressionsMenu), true);*/
            EditorGUILayout.Space();
            paramSuffix = EditorGUILayout.TextField("Converted Parameter Suffix", paramSuffix);
            defaultEmulatorModeOn = EditorGUILayout.Toggle("Emulation Mode On By Default", defaultEmulatorModeOn);
            toggleParam = EditorGUILayout.TextField("Emulation Mode Toggle Parameter Name", toggleParam);

            EditorGUILayout.Space();
            if (GUILayout.Button("Refresh Layer List"))
            {
                GuessRelevantLayers();
            }
            var headerRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(25));
            EditorGUILayout.Space();
            colHeader.OnGUI(headerRect, 0);
            EditorGUILayout.EndHorizontal();

            foreach (AnimatorLayerInfo x in layerSelections)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(x.index.ToString(), GUILayout.Width(colHeader.GetColumn(0).width));
                EditorGUILayout.LabelField(x.name, GUILayout.MinWidth(colHeader.GetColumn(1).width));
                EditorGUILayout.LabelField(x.ratio.ToString("P"), GUILayout.MinWidth(colHeader.GetColumn(2).width));
                x.selected = EditorGUILayout.Toggle(x.selected, GUILayout.MinWidth(colHeader.GetColumn(3).width));
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Convert Selected Layers"))
            {
                Convert();
            }
        }

/*        private void PopulateFieldsFromAvatar()
        {
            fxAnimator = (AnimatorController)avatar.baseAnimationLayers[4].animatorController;
            expressionParameters = avatar.expressionParameters;
            if (expressionParameters == null)
            {
                string destFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(fxAnimator));

            }
            expressionsMenu = avatar.expressionsMenu;
        }*/

        private void GuessRelevantLayers()
        {
            fxAnimator = (AnimatorController)avatar.baseAnimationLayers[4].animatorController;
            wdOn = false;
            var ratios = new List<float>();
            foreach (var layer in fxAnimator.layers)
            {
                var paramNames = new List<string>();
                foreach (var state in layer.stateMachine.states)
                {
                    wdOn = wdOn || state.state.writeDefaultValues;
                    foreach (var t in state.state.transitions)
                    {
                        foreach (var c in t.conditions)
                        {
                            paramNames.Add(c.parameter);
                        }
                    }
                }
                foreach (var t in layer.stateMachine.anyStateTransitions)
                {
                    foreach (var c in t.conditions)
                    {
                        paramNames.Add(c.parameter);
                    }
                }
                foreach (var t in layer.stateMachine.entryTransitions)
                {
                    foreach (var c in t.conditions)
                    {
                        paramNames.Add(c.parameter);
                    }
                }

                var gestureParams = new List<string>(
                    from pName in paramNames
                    where pName == "GestureLeft" || pName == "GestureRight"
                    select pName
                );

                float ratio = (float)gestureParams.Count / (float)paramNames.Count;
                ratio = float.IsNaN(ratio) || float.IsInfinity(ratio) ? 0 : ratio;
                ratios.Add(ratio);
            }

            layerSelections.Clear();
            for (int i = 0; i < ratios.Count; ++i)
            {
                if (ratios[i] > 0)
                {
                    var layer = fxAnimator.layers[i];
                    layerSelections.Add(new AnimatorLayerInfo {
                        index = i,
                        name = layer.name,
                        ratio = ratios[i],
                        selected = false
                    });
                }
            }
        }

/*        private void ReplaceAssetsWithClones(dir)
        {
            string suffix = System.DateTime.Now.ToFileTime().ToString();
            fxAnimator = CloneAsset(fxAnimator, suffix);
            expressionParameters = CloneAsset(expressionParameters, suffix);
            expressionsMenu = CloneAsset(expressionsMenu, suffix);

            avatar.baseAnimationLayers[4].animatorController = fxAnimator;
            avatar.expressionParameters = expressionParameters;
            avatar.expressionsMenu = expressionsMenu;
        }*/

        private T CloneAsset<T>(T original, string directory, string newName = null) where T : UnityEngine.Object
        {
/*            if (directory.Last() != Path.DirectorySeparatorChar && directory.Last() != Path.AltDirectorySeparatorChar)
            {
                directory += Path.DirectorySeparatorChar;
            }*/
            string originalPath = AssetDatabase.GetAssetPath(original);
            newName = newName == null || newName == "" ? Path.GetFileNameWithoutExtension(originalPath) : newName;
            newName = Path.GetFileNameWithoutExtension(newName);
            string extension = Path.GetExtension(originalPath);
            string newPath = Path.Combine(directory, $"{newName}{extension}");
            AssetDatabase.CopyAsset(originalPath, newPath);
            T clone = AssetDatabase.LoadAssetAtPath<T>(newPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return clone;
        }

        private void Convert()
        {
            fxAnimator = (AnimatorController)avatar.baseAnimationLayers[4].animatorController;
            string subFolder = $"{avatar.gameObject.name}_{System.DateTime.Now.ToFileTime()}";
            string guid = AssetDatabase.CreateFolder(GENERATED_ASSET_DIRECTORY, subFolder);
            string currentGeneratedAssetDir = AssetDatabase.GUIDToAssetPath(guid);
            if (currentGeneratedAssetDir == "")
            {
                throw new System.Exception("Error creating folder for generated assets");
            }
            Debug.Log($"Storing new assets in {currentGeneratedAssetDir}");
            fxAnimator = CloneAsset(fxAnimator, currentGeneratedAssetDir);
            avatar.baseAnimationLayers[(int)AV3ManagerFunctionWrapper.PlayableLayer.FX].animatorController = fxAnimator;

            newParamsToAdd.Clear();
            fxAnimator.AddParameter(toggleParam, AnimatorControllerParameterType.Bool);
            //newParamsToAdd.Add(toggleParam, AnimatorControllerParameterType.Bool);


            var layersToConvert =
                from ls in layerSelections
                where ls.selected
                select ls.index;

            var newLayerIndices = new List<int>();
            var layers = fxAnimator.layers;
            foreach (var layerIndex in layersToConvert)
            {
                layers[layerIndex] = ConvertLayerInPlace(layers[layerIndex]);
            }
            fxAnimator.layers = layers;

            var newParamsProcessed = new List<VRCExpressionParameters.Parameter>(
                from item in newParamsToAdd
                select (new VRCExpressionParameters.Parameter()
                {
                    defaultValue = 0,
                    saved = false,
                    name = item.Key,
                    valueType = paramTypeLookup[item.Value]
                }));

            var toggleParamObject = new VRCExpressionParameters.Parameter()
            {
                defaultValue = 1,
                saved = true,
                name = toggleParam,
                valueType = ValueType.Bool
            };
            newParamsProcessed.Add(toggleParamObject);

            AV3ManagerFunctionWrapper.AddParameters(
                avatar,
                newParamsProcessed,
                currentGeneratedAssetDir + "/"
            );
            var dummyMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();

            AV3ManagerFunctionWrapper.AddSubMenu(
                avatar,
                dummyMenu,
                "Gesture Emulation Mode",
                currentGeneratedAssetDir + "/"
            );

            var exprMenu = avatar.expressionsMenu;
            exprMenu.controls.Remove(exprMenu.controls.Last());
            var toggleControl = new VRCExpressionsMenu.Control()
            {
                name = "Gesture Emulation Mode",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = toggleParam },
                icon = icon
            };
            exprMenu.controls.Add(toggleControl);
            EditorUtility.SetDirty(exprMenu);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private AnimatorControllerLayer ConvertLayerInPlace(AnimatorControllerLayer layer)
        {
            layer.stateMachine = ConvertStateMachineInPlace(layer.stateMachine);
            return layer;
        }

        private AnimatorStateMachine ConvertStateMachineInPlace(AnimatorStateMachine sm)
        {
            foreach (var state in sm.states)
            {
                var newTransitions = new List<AnimatorStateTransition>();
                foreach (int i in Enumerable.Range(0, state.state.transitions.Length))
                {
                    var tr = state.state.transitions[i];
                    if (IsTransitionRelevant(tr))
                    {
                        var altTransition = state.state.AddTransition(tr.destinationState);
                        ApplyTransitionSettings(tr, altTransition);
                        altTransition = ConvertTransitionInPlace(altTransition);
                        newTransitions.Add(altTransition);
                        tr.AddCondition(AnimatorConditionMode.IfNot, 1, toggleParam);
                    }
                    newTransitions.Add(tr);

                }
                state.state.transitions = newTransitions.ToArray();
            }

            var newAnyStateTransitions = new List<AnimatorStateTransition>();
            foreach (int i in Enumerable.Range(0, sm.anyStateTransitions.Length))
            {
                var tr = sm.anyStateTransitions[i];
                if (IsTransitionRelevant(tr))
                {
                    var altTransition = sm.AddAnyStateTransition(tr.destinationState);
                    ApplyTransitionSettings(tr, altTransition);
                    altTransition = ConvertTransitionInPlace(altTransition);
                    newAnyStateTransitions.Add(altTransition);
                    tr.AddCondition(AnimatorConditionMode.IfNot, 1, toggleParam);
                }
                newAnyStateTransitions.Add(tr);
            }
            sm.anyStateTransitions = newAnyStateTransitions.ToArray();

            var newEntryTransitions = new List<AnimatorTransition>();
            foreach (int i in Enumerable.Range(0, sm.entryTransitions.Length))
            {
                var tr = sm.entryTransitions[i];
                if (IsTransitionRelevant(tr))
                {
                    var altTransition = sm.AddEntryTransition(tr.destinationState);
                    ApplyTransitionSettings(tr, altTransition);
                    altTransition = ConvertTransitionInPlace(altTransition);
                    newEntryTransitions.Add(altTransition);
                    tr.AddCondition(AnimatorConditionMode.IfNot, 1, toggleParam);
                }
                newEntryTransitions.Add(tr);
            }
            sm.entryTransitions = newEntryTransitions.ToArray();

            var newSubStateMachines = new ChildAnimatorStateMachine[sm.stateMachines.Length];
            foreach (int i in Enumerable.Range(0, sm.stateMachines.Length))
            {
                var ssm = sm.stateMachines[i];
                var nssm = ConvertStateMachineInPlace(ssm.stateMachine);
                newSubStateMachines[i] = new ChildAnimatorStateMachine() { position = ssm.position, stateMachine = nssm };
            }

            return sm;
        }

        private bool IsTransitionRelevant(AnimatorTransitionBase tr)
        {
            bool relevant = false;
            foreach (var cond in tr.conditions)
            {
                foreach (var item in paramsToConvert.AsEnumerable())
                {
                    if (cond.parameter == item.Key)
                    {
                        relevant = true;
                        break;
                    }
                }
            }

            return relevant;
        }

        private T ConvertTransitionInPlace<T>(T tr) where T : AnimatorTransitionBase
        {
            var newConditions = new AnimatorCondition[tr.conditions.Length + 1];
            foreach (int i in Enumerable.Range(0, tr.conditions.Length))
            {
                var cond = tr.conditions[i];
                string param = cond.parameter;

                foreach (var item in paramsToConvert.AsEnumerable())
                {
                    if (cond.parameter == item.Key)
                    {
                        param = param + paramSuffix;
                        if (!newParamsToAdd.ContainsKey(param))
                        {
                            newParamsToAdd.Add(param, item.Value);
                            fxAnimator.AddParameter(param, item.Value);
                        }
                        break;
                    }
                }

                newConditions[i] = new AnimatorCondition() { mode = cond.mode, threshold = cond.threshold, parameter = param };
            }
            newConditions[tr.conditions.Length] = new AnimatorCondition() { mode = AnimatorConditionMode.If, threshold = 1, parameter = toggleParam };

            tr.conditions = newConditions;

            return tr;
        }

        // helper class
        public class AnimatorLayerInfo
        {
            public int index;
            public string name;
            public float ratio;
            public bool selected;

/*            public AnimatorLayerInfo(int index, string name, float ratio, bool selected)
            {
                this.index = index;
                this.name = name;
                this.ratio = ratio;
                this.selected = selected;
            }*/
        }

        // copypasted from VRLabs AnimatorCloner.cs, but shouldn't change b/c it's not dependent on VRC Api
        private static void ApplyTransitionSettings(AnimatorStateTransition transition, AnimatorStateTransition newTransition)
        {
            newTransition.canTransitionToSelf = transition.canTransitionToSelf;
            newTransition.duration = transition.duration;
            newTransition.exitTime = transition.exitTime;
            newTransition.hasExitTime = transition.hasExitTime;
            newTransition.hasFixedDuration = transition.hasFixedDuration;
            newTransition.hideFlags = transition.hideFlags;
            newTransition.isExit = transition.isExit;
            newTransition.mute = transition.mute;
            newTransition.name = transition.name;
            newTransition.offset = transition.offset;
            newTransition.interruptionSource = transition.interruptionSource;
            newTransition.orderedInterruption = transition.orderedInterruption;
            newTransition.solo = transition.solo;
            foreach (var condition in transition.conditions)
                newTransition.AddCondition(condition.mode, condition.threshold, condition.parameter);

        }

        private static void ApplyTransitionSettings(AnimatorTransition transition, AnimatorTransition newTransition)
        {
            newTransition.hideFlags = transition.hideFlags;
            newTransition.isExit = transition.isExit;
            newTransition.mute = transition.mute;
            newTransition.name = transition.name;
            newTransition.solo = transition.solo;
            foreach (var condition in transition.conditions)
                newTransition.AddCondition(condition.mode, condition.threshold, condition.parameter);

        }
    }

    public static class AV3ManagerFunctionWrapper
    {
        public static System.Type AV3ManagerFunctionsType;

        public static System.Type GetAV3ManagerFunctions()
        {
            if (AV3ManagerFunctionsType == null)
            {
                Debug.Log("checking again");
                AV3ManagerFunctionsType = System.AppDomain.CurrentDomain
                    .GetAssemblies()
                    .SelectMany(x => x.GetTypes())
                    .FirstOrDefault(x => x.FullName?.Equals("VRLabs.AV3Manager.AV3ManagerFunctions") ?? false);
            }

            return AV3ManagerFunctionsType;
        }
        
        public static bool CheckForAV3ManagerFunctions()
        {
            return GetAV3ManagerFunctions() != null;
        }

        public static void AddParameters(VRCAvatarDescriptor descriptor, IEnumerable<VRCExpressionParameters.Parameter> parameters, string directory, bool overwrite = true)
        {
            var paramsList = new object[] {
                descriptor,
                parameters,
                directory,
                overwrite
            };
/*            Debug.Log(GetAV3ManagerFunctions() != null);
            var method = GetAV3ManagerFunctions().GetMethod(
                "AddParameters",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
            );
            Debug.Log(method != null);*/
            GetAV3ManagerFunctions().GetMethod("AddParameters").Invoke(new object(), paramsList);
        }

        public static void AddSubMenu(VRCAvatarDescriptor descriptor, VRCExpressionsMenu menuToAdd, string controlName, string directory, VRCExpressionsMenu.Control.Parameter controlParameter = null, Texture2D icon = null, bool overwrite = true)
        {
            var paramsList = new object[] {
                descriptor,
                menuToAdd,
                controlName,
                directory,
                controlParameter,
                icon,
                overwrite
            };
            GetAV3ManagerFunctions().GetMethod("AddSubMenu").Invoke(new object(), paramsList);
        }

        public enum PlayableLayer // copy-pasted. these won't change, right? right?
        {
            Base = 0,
            Additive = 1,
            Gesture = 2,
            Action = 3,
            FX = 4
        }
    }
}
