using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;
using _21tools.Runtime;
using System.Collections.Generic;

[assembly: ExportsPlugin(typeof(_21tools.Editor.NDMF.RotationConstraintPlugin))]

namespace _21tools.Editor.NDMF
{
    public class RotationConstraintPlugin : Plugin<RotationConstraintPlugin>
    {
        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming).Run("Apply Prosthetic Arm Rotation Constraints", ctx =>
            {
                ApplyRotationConstraints(ctx);
            });
        }

        private void ApplyRotationConstraints(BuildContext ctx)
        {
            // ルートGameObject取得
            GameObject avatarRoot = ctx.AvatarRootObject;
            
            // Animator取得
            Animator animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || animator.avatar == null)
            {
                Debug.LogWarning("ProstheticArmConstraint: Animator component or Avatar is not set up on the avatar.");
                return;
            }

            // 同コンポーネント検索
            ProstheticArmConstraint[] prostheticConstraints = avatarRoot.GetComponentsInChildren<ProstheticArmConstraint>();

            foreach (ProstheticArmConstraint prostheticConstraint in prostheticConstraints)
            {
                ProcessProstheticConstraint(prostheticConstraint, animator);
            }
        }

        private void ProcessProstheticConstraint(ProstheticArmConstraint prostheticConstraint, Animator animator)
        {
            if (prostheticConstraint.ProstheticArmRoot == null)
            {
                Debug.LogWarning($"ProstheticArmConstraint: Prosthetic arm root of {prostheticConstraint.gameObject.name} is not set.");
                return;
            }

            // ルートボーン取得
            Transform avatarSourceRoot = animator.GetBoneTransform(prostheticConstraint.AvatarSourceRootBone);
            if (avatarSourceRoot == null)
            {
                Debug.LogWarning($"ProstheticArmConstraint: Could not find {prostheticConstraint.AvatarSourceRootBone} bone on the avatar.");
                return;
            }

            Debug.Log($"ProstheticArmConstraint: Using avatar's source root bone {prostheticConstraint.AvatarSourceRootBone}: {avatarSourceRoot.name}");

            foreach (var boneMapping in prostheticConstraint.BoneMappings)
            {
                if (boneMapping.ProstheticBone == null)
                {
                    Debug.LogWarning("ProstheticArmConstraint: A bone mapping has an unset prosthetic bone.");
                    continue;
                }

                // 対応ボーンを取得
                Transform avatarBone = animator.GetBoneTransform(boneMapping.AvatarBoneType);
                if (avatarBone == null)
                {
                    Debug.LogWarning($"ProstheticArmConstraint: Could not find {boneMapping.AvatarBoneType} bone on the avatar.");
                    continue;
                }

                if (!IsDescendantOf(avatarBone, avatarSourceRoot))
                {
                    Debug.LogWarning($"ProstheticArmConstraint: Avatar bone {avatarBone.name} ({boneMapping.AvatarBoneType}) is not a descendant of the specified source root bone {avatarSourceRoot.name} ({prostheticConstraint.AvatarSourceRootBone}). Skipping.");
                    continue;
                }

                // RotationConstraint追加
                RotationConstraint rotationConstraint = boneMapping.ProstheticBone.gameObject.GetComponent<RotationConstraint>();
                if (rotationConstraint == null)
                {
                    rotationConstraint = boneMapping.ProstheticBone.gameObject.AddComponent<RotationConstraint>();
                }

                // ソース設定
                List<ConstraintSource> sources = new List<ConstraintSource>();
                ConstraintSource source = new ConstraintSource();
                source.sourceTransform = avatarBone;
                source.weight = 1.0f;
                sources.Add(source);

                rotationConstraint.SetSources(sources);
                rotationConstraint.constraintActive = true;
                rotationConstraint.locked = true;
                rotationConstraint.rotationOffset = boneMapping.RotationOffset;

                Debug.Log($"ProstheticArmConstraint: Applied Rotation Constraint to {boneMapping.ProstheticBone.name} from {avatarBone.name} ({boneMapping.AvatarBoneType}). Offset: {boneMapping.RotationOffset}");
            }

            // 元のコンポーネント削除
            Object.DestroyImmediate(prostheticConstraint);
        }

        private bool IsDescendantOf(Transform child, Transform parent)
        {
            if (child == null || parent == null)
                return false;

            Transform current = child;
            while (current != null)
            {
                if (current == parent)
                    return true;
                current = current.parent;
            }
            return false;
        }
    }
}