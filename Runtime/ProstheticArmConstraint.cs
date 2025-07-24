using UnityEngine;
using System.Collections.Generic;

namespace net.yuleo21.prostheticarmconstraint.Runtime
{
    [AddComponentMenu("21tools/Prosthetic Arm Constraint")]
    public class ProstheticArmConstraint : MonoBehaviour
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