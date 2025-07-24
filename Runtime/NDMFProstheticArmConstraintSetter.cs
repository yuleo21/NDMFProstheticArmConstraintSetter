using UnityEngine;
using System.Collections.Generic;

namespace net.yuleo21.ndmfprostheticarmconstraintsetter.Runtime
{
    [AddComponentMenu("21tools/NDMF Prosthetic Arm Constraint Setter")]
    public class NDMFProstheticArmConstraintSetter : MonoBehaviour
    {
        public GameObject ProstheticArmRoot;
        public HumanBodyBones AvatarSourceRootBone = HumanBodyBones.Chest;

        [System.Serializable]
        public class BoneMapping
        {
            public Transform ProstheticBone;
            public HumanBodyBones AvatarBoneType;
            public Vector3 RotationOffset;
        }

        public List<BoneMapping> BoneMappings = new List<BoneMapping>();
    }
}