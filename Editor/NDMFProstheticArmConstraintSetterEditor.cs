using UnityEditor;
using UnityEngine;
using net.yuleo21.ndmfprostheticarmconstraintsetter.Runtime;
using System.Linq;
using System.Collections.Generic;

namespace net.yuleo21.ndmfprostheticarmconstraintsetter.Editor
{
    [CustomEditor(typeof(NDMFProstheticArmConstraintSetter))]
    public class NDMFProstheticArmConstraintSetterEditor : UnityEditor.Editor
    {
        private SerializedProperty boneMappingsProp;

        private void OnEnable()
        {
            boneMappingsProp = serializedObject.FindProperty("BoneMappings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, "m_Script", "BoneMappings");

            NDMFProstheticArmConstraintSetter myTarget = (NDMFProstheticArmConstraintSetter)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Humanoid Bone Mapping Information", EditorStyles.boldLabel);

            if (myTarget.ProstheticArmRoot != null)
            {
                // アバターを検索
                GameObject avatarRoot = FindAvatarRoot(myTarget.gameObject);
                
                if (avatarRoot != null)
                {
                    EditorGUILayout.LabelField($"Avatar Root: {avatarRoot.name}");
                    EditorGUILayout.LabelField($"Source Root Bone: {myTarget.AvatarSourceRootBone}");
                    
                    if (GUILayout.Button("Auto Map Bones"))
                    {
                        AutoMapBones(myTarget, avatarRoot);
                    }
                    
                    if (GUILayout.Button("Clear Mappings"))
                    {
                        myTarget.BoneMappings.Clear();
                        EditorUtility.SetDirty(myTarget);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Avatar root not found. Put this GameObject at the Avatar root.", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Please set the Prosthetic Arm Root GameObject.", MessageType.Info);
            }

            // BoneMappingsリスト
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bone Mappings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(boneMappingsProp, true);

            serializedObject.ApplyModifiedProperties();
            if (GUI.changed)
            {
                EditorUtility.SetDirty(myTarget);
            }
        }

        private GameObject FindAvatarRoot(GameObject startObject)
        {
            Transform current = startObject.transform;
            
            while (current != null)
            {
                if (current.GetComponent("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor") != null)
                {
                    return current.gameObject;
                }
                current = current.parent;
            }
            
            return null;
        }

        // 義手ボーンをアバターにマッピング
        private void AutoMapBones(NDMFProstheticArmConstraintSetter component, GameObject avatarRoot)
        {
            Animator animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                EditorUtility.DisplayDialog("Error", "Animator component, Avatar, or Humanoid rig is not set up on the avatar.", "OK");
                return;
            }

            Transform avatarSourceRoot = animator.GetBoneTransform(component.AvatarSourceRootBone);
            if (avatarSourceRoot == null)
            {
                EditorUtility.DisplayDialog("Error", $"Could not find {component.AvatarSourceRootBone} bone on the avatar. Check AvatarSourceRootBone setting.", "OK");
                return;
            }

            // アバターTransform取得
            Transform[] avatarBones = avatarSourceRoot.GetComponentsInChildren<Transform>(true); 
            
            // 義手Transformを取得
            Transform[] prostheticBones = component.ProstheticArmRoot.GetComponentsInChildren<Transform>(true);
            
            component.BoneMappings.Clear();
            int mappedCount = 0;

            foreach (Transform prostheticBone in prostheticBones)
            {
                // ルートはマッピング対象外
                if (prostheticBone == component.ProstheticArmRoot.transform)
                    continue;

                // 義手ボーン名から検索
                Transform bestMatchAvatarBone = FindBestMatchingBone(prostheticBone, avatarBones);
                
                if (bestMatchAvatarBone != null)
                {
                    // Humanoidボーンタイプ取得
                    HumanBodyBones humanoidBoneType = GetHumanoidBoneTypeFromTransform(bestMatchAvatarBone, animator, prostheticBone.name);
                    
                    if (humanoidBoneType != HumanBodyBones.LastBone)
                    {
                        NDMFProstheticArmConstraintSetter.BoneMapping mapping = new NDMFProstheticArmConstraintSetter.BoneMapping();
                        mapping.ProstheticBone = prostheticBone;
                        mapping.AvatarBoneType = humanoidBoneType;
						// 回転差分をオフセットとして計算
						Quaternion prostheticRotation = prostheticBone.rotation;
						Quaternion avatarRotation = bestMatchAvatarBone.rotation;
						Quaternion delta = Quaternion.Inverse(avatarRotation) * prostheticRotation;
						Vector3 eulerOffset = delta.eulerAngles;

						// オイラー角を正規化
						eulerOffset = new Vector3(
							Mathf.DeltaAngle(0, eulerOffset.x),
							Mathf.DeltaAngle(0, eulerOffset.y),
							Mathf.DeltaAngle(0, eulerOffset.z)
						);

						mapping.RotationOffset = eulerOffset;

                        component.BoneMappings.Add(mapping);
                        mappedCount++;
                        
                        Debug.Log($"Mapped: {prostheticBone.name} -> {bestMatchAvatarBone.name} ({humanoidBoneType})");
                    }
                    else
                    {
                        Debug.LogWarning($"Skipped: Could not determine appropriate HumanoidBoneType for {bestMatchAvatarBone.name} based on prosthetic bone {prostheticBone.name}. Check if prosthetic bone name indicates side correctly.");
                    }
                }
                else
                {
                    Debug.Log($"No good match found for prosthetic bone: {prostheticBone.name}");
                }
            }

            EditorUtility.SetDirty(component);
            EditorUtility.DisplayDialog("Auto Mapping Complete", $"{mappedCount} bones were mapped.", "OK");
        }

        // 義手ボーン名から検索
        private Transform FindBestMatchingBone(Transform prostheticBone, Transform[] avatarBones)
        {
            string prostheticName = prostheticBone.name.ToLower();
            Transform bestMatch = null;
            float bestScore = 0f;

            // 左右情報取得
            Side prostheticSide = GetBoneSide(prostheticName);

            foreach (Transform avatarBone in avatarBones)
            {
                string avatarName = avatarBone.name.ToLower();
                Side avatarSide = GetBoneSide(avatarName);

                // 左右なしをはスキップ
                if (prostheticSide != Side.Undefined && avatarSide != Side.Undefined && prostheticSide != avatarSide)
                {
                    continue;
                }

                // 類似度計算
                float score = CalculateBoneNameSimilarity(prostheticName, avatarName);
                
                // 類似度評価
                if (score > bestScore && score > 0.3f) 
                {
                    bestScore = score;
                    bestMatch = avatarBone;
                }
            }

            return bestMatch;
        }

        // 類似度計算
        private float CalculateBoneNameSimilarity(string name1, string name2)
        {
            string[] keywords = { "shoulder", "upper", "lower", "hand", "thumb", "index", "middle", "ring", "little", "pinky", "arm", "elbow", "wrist", "clavicle", "sleeve", "forearm", "upperarm", "fingers", "phalange", "digit", "carpals" };
            
            int commonKeywords = 0;
            int totalKeywords = 0;
            
            foreach (string keyword in keywords)
            {
                bool name1HasKeyword = name1.Contains(keyword);
                bool name2HasKeyword = name2.Contains(keyword);
                
                if (name1HasKeyword || name2HasKeyword)
                {
                    totalKeywords++;
                    if (name1HasKeyword && name2HasKeyword)
                    {
                        commonKeywords++;
                    }
                }
            }
            
            // レーベンシュタイン距離を計算 https://note.com/noa813/n/nb7ffd5a8f5e9
            float levenshteinSimilarity = 1.0f - ((float)LevenshteinDistance(name1, name2) / Mathf.Max(name1.Length, name2.Length));

            // キーワードマッチングとレーベンシュタイン距離を組み合わせて評価
            float combinedScore = 0.7f * levenshteinSimilarity + 0.3f * ((totalKeywords == 0) ? 0f : (float)commonKeywords / totalKeywords);

            return combinedScore;
        }

        // レーベンシュタイン距離を計算
        private int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Mathf.Min(
                        Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        // 左右判定Enum
        private enum Side { Undefined, Left, Right }

        // ボーン名から左右判定
        private Side GetBoneSide(string boneName)
        {
            boneName = boneName.ToLower();
            string[] leftIndicators = { "left", "l_", "_l", "-l", ".l", "lhand", "larm", "lshoulder", "l.001", "hand_l", "arm_l", "lowerarm.l", "upperarm.l", "elbow_l", "wrist_l" };
            string[] rightIndicators = { "right", "r_", "_r", "-r", ".r", "rhand", "rarm", "rshoulder", "r.001", "hand_r", "arm_r", "lowerarm.r", "upperarm.r", "elbow_r", "wrist_r" };

            if (leftIndicators.Any(s => boneName.Contains(s))) return Side.Left;
            if (rightIndicators.Any(s => boneName.Contains(s))) return Side.Right;

            return Side.Undefined;
        }

        // TransformからHumanoidボーンタイプ取得
        private HumanBodyBones GetHumanoidBoneTypeFromTransform(Transform bone, Animator animator, string prostheticBoneName)
        {
            Side prostheticSide = GetBoneSide(prostheticBoneName);

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                HumanBodyBones boneType = (HumanBodyBones)i;
                Transform humanoidBone = animator.GetBoneTransform(boneType);
                
                if (humanoidBone == bone)
                {
                    Side humanoidBoneSide = GetHumanoidBoneSide(boneType);
                    if (prostheticSide != Side.Undefined && humanoidBoneSide != Side.Undefined && prostheticSide != humanoidBoneSide)
                    {
                        continue;
                    }
                    return boneType;
                }
            }

            foreach (HumanBodyBones boneType in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (boneType == HumanBodyBones.LastBone || boneType == HumanBodyBones.Hips) continue; 

                Transform humanoidBone = animator.GetBoneTransform(boneType);
                if (humanoidBone == null) continue;

                Side humanoidBoneSide = GetHumanoidBoneSide(boneType);

                if (prostheticSide != Side.Undefined && humanoidBoneSide != Side.Undefined && prostheticSide != humanoidBoneSide)
                {
                    continue;
                }

                float score = CalculateBoneNameSimilarity(bone.name.ToLower(), humanoidBone.name.ToLower());
                
                if (IsStrongMatch(prostheticBoneName, boneType, score))
                {
                    if (prostheticSide == Side.Undefined || humanoidBoneSide == Side.Undefined || prostheticSide == humanoidBoneSide)
                    {
                        return boneType;
                    }
                }
            }

            return HumanBodyBones.LastBone;
        }

        // HumanoidBoneTypeから左右判定
        private Side GetHumanoidBoneSide(HumanBodyBones boneType)
        {
            switch (boneType)
            {
                case HumanBodyBones.LeftShoulder:
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.LeftThumbProximal:
                case HumanBodyBones.LeftThumbIntermediate:
                case HumanBodyBones.LeftThumbDistal:
                case HumanBodyBones.LeftIndexProximal:
                case HumanBodyBones.LeftIndexIntermediate:
                case HumanBodyBones.LeftIndexDistal:
                case HumanBodyBones.LeftMiddleProximal:
                case HumanBodyBones.LeftMiddleIntermediate:
                case HumanBodyBones.LeftMiddleDistal:
                case HumanBodyBones.LeftRingProximal:
                case HumanBodyBones.LeftRingIntermediate:
                case HumanBodyBones.LeftRingDistal:
                case HumanBodyBones.LeftLittleProximal:
                case HumanBodyBones.LeftLittleIntermediate:
                case HumanBodyBones.LeftLittleDistal:
                    return Side.Left;

                case HumanBodyBones.RightShoulder:
                case HumanBodyBones.RightUpperArm:
                case HumanBodyBones.RightLowerArm:
                case HumanBodyBones.RightHand:
                case HumanBodyBones.RightThumbProximal:
                case HumanBodyBones.RightThumbIntermediate:
                case HumanBodyBones.RightThumbDistal:
                case HumanBodyBones.RightIndexProximal:
                case HumanBodyBones.RightIndexIntermediate:
                case HumanBodyBones.RightIndexDistal:
                case HumanBodyBones.RightMiddleProximal:
                case HumanBodyBones.RightMiddleIntermediate:
                case HumanBodyBones.RightMiddleDistal:
                case HumanBodyBones.RightRingProximal:
                case HumanBodyBones.RightRingIntermediate:
                case HumanBodyBones.RightRingDistal:
                case HumanBodyBones.RightLittleProximal:
                case HumanBodyBones.RightLittleIntermediate:
                case HumanBodyBones.RightLittleDistal:
                    return Side.Right;

                default:
                    return Side.Undefined; // 左右がないボーン
            }
        }

        private bool IsStrongMatch(string prostheticBoneName, HumanBodyBones humanoidBoneType, float similarityScore)
        {
            Side prostheticSide = GetBoneSide(prostheticBoneName);
            Side humanoidSide = GetHumanoidBoneSide(humanoidBoneType);

            if (prostheticSide != Side.Undefined && humanoidSide != Side.Undefined && prostheticSide != humanoidSide)
                return false;

            string typeName = humanoidBoneType.ToString().ToLower();
            if (similarityScore > 0.6f) return true;

            // 共通左右キーワードチェック
            bool typeHasLeft = typeName.Contains("left");
            bool typeHasRight = typeName.Contains("right");
            bool prostheticHasLeft = prostheticSide == Side.Left;
            bool prostheticHasRight = prostheticSide == Side.Right;

            if ((typeHasLeft && prostheticHasLeft) || (typeHasRight && prostheticHasRight)) return true;
            
            string strippedTypeName = typeName.Replace("left", "").Replace("right", "");
            string strippedProstheticName = prostheticBoneName.Replace("left", "").Replace("right", "").Replace("l_", "").Replace("r_", "").Replace("_l", "").Replace("_r", "").Replace("-l", "").Replace("-r", "").Replace(".l", "").Replace(".r", ""); // .l/.r も除去
            if (!string.IsNullOrEmpty(strippedProstheticName) && strippedTypeName.Contains(strippedProstheticName)) return true;

            string humanBodyPartName = GetHumanBodyPartName(humanoidBoneType);
            if (!string.IsNullOrEmpty(humanBodyPartName) && prostheticBoneName.Contains(humanBodyPartName))
            {
                return true;
            }
            return false;
        }

        // 部位の名前取得
        private string GetHumanBodyPartName(HumanBodyBones boneType)
        {
            string typeName = boneType.ToString().ToLower();
            typeName = typeName.Replace("left", "").Replace("right", "");
            typeName = typeName.Replace("proximal", "").Replace("intermediate", "").Replace("distal", "");
            typeName = typeName.Replace("_", " ").Trim();
            return typeName;
        }
    }
}