using System;
using System.Collections.Generic;
using UnityEngine;

public class CustomOptitrackSkeletonAnimator : MonoBehaviour
{
    /// <summary>The client object to use for receiving streamed skeletal pose data.</summary>
    [Tooltip("The object containing the OptiTrackStreamingClient script.")]
    public PlayableOptitrackStreamingClient StreamingClient;

    /// <summary>The name of the skeleton asset in the stream that will provide retargeting source data.</summary>
    [Tooltip("The name of skeleton asset in Motive.")]
    public string SkeletonAssetName = "Skeleton1";

    /// <summary>The humanoid avatar for this GameObject's imported model.</summary>
    [Tooltip("The humanoid avatar model.")]
    public Avatar DestinationAvatar;

    [Header("Debug")]
    public bool connectBones = true;

    #region Private fields
    /// <summary>Used when retrieving and retargeting source pose. Cached and reused for efficiency.</summary>
    private HumanPose m_humanPose = new HumanPose();

    /// <summary>The streamed source skeleton definition.</summary>
    private OptitrackSkeletonDefinition m_skeletonDef;

    /// <summary>The root GameObject of the streamed skeletal pose transform hierarchy.</summary>
    private GameObject m_rootObject;

    /// <summary>Maps between OptiTrack skeleton bone IDs and corresponding GameObjects.</summary>
    private Dictionary<Int32, GameObject> m_boneObjectMap;

    /// <summary>
    /// Maps between Mecanim anatomy bone names (keys) and streamed bone names from various naming conventions
    /// supported by the OptiTrack software (values). Populated by <see cref="CacheBoneNameMap"/>.
    /// </summary>
    private Dictionary<string, string> m_cachedMecanimBoneNameMap = new Dictionary<string, string>();

    /// <summary>Created automatically based on <see cref="m_skeletonDef"/>.</summary>
    private Avatar m_srcAvatar;

    /// <summary>Set up to read poses from our streamed GameObject transform hierarchy.</summary>
    private HumanPoseHandler m_srcPoseHandler;

    /// <summary>Set up to write poses to this GameObject using <see cref="DestinationAvatar"/>.</summary>
    private HumanPoseHandler m_destPoseHandler;
    #endregion Private fields

    void Start()
    {
        // If the user didn't explicitly associate a client, find a suitable default.
        if (this.StreamingClient == null)
        {
            this.StreamingClient = PlayableOptitrackStreamingClient.FindDefaultClient();

            // If we still couldn't find one, disable this component.
            if (this.StreamingClient == null)
            {
                Debug.LogError(GetType().FullName + ": Streaming client not set, and no " + typeof(PlayableOptitrackStreamingClient).FullName + " components found in scene; disabling this component.", this);
                this.enabled = false;
                return;
            }
        }

        this.StreamingClient.RegisterSkeleton(this.SkeletonAssetName);

        // Create a lookup from Mecanim anatomy bone names to OptiTrack streaming bone names.
        CacheBoneNameMap(this.StreamingClient.BoneNamingConvention, this.SkeletonAssetName);

        // Retrieve the OptiTrack skeleton definition.
        m_skeletonDef = this.StreamingClient.GetSkeletonDefinitionByName(this.SkeletonAssetName);

        if (m_skeletonDef == null)
        {
            Debug.LogError(GetType().FullName + ": Could not find skeleton definition with the name \"" + this.SkeletonAssetName + "\"", this);
            this.enabled = false;
            return;
        }

        // Create a hierarchy of GameObjects that will receive the skeletal pose data.
        string rootObjectName = "OptiTrack Skeleton - " + this.SkeletonAssetName;
        m_rootObject = new GameObject(rootObjectName);

        m_boneObjectMap = new Dictionary<Int32, GameObject>(m_skeletonDef.Bones.Count);

        for (int boneDefIdx = 0; boneDefIdx < m_skeletonDef.Bones.Count; ++boneDefIdx)
        {
            OptitrackSkeletonDefinition.BoneDefinition boneDef = m_skeletonDef.Bones[boneDefIdx];

            GameObject boneObject = new GameObject(boneDef.Name);
            boneObject.transform.parent = boneDef.ParentId == 0 ? m_rootObject.transform : m_boneObjectMap[boneDef.ParentId].transform;
            boneObject.transform.localPosition = boneDef.Offset;
            m_boneObjectMap[boneDef.Id] = boneObject;

            if (connectBones)
            {
                FromToLine deb = boneObject.AddComponent<FromToLine>();
                deb.target = boneDef.ParentId == 0 ? m_rootObject.transform : m_boneObjectMap[boneDef.ParentId].transform;
            }
        }

        // Hook up retargeting between those GameObjects and the destination Avatar.
        MecanimSetup(rootObjectName);

        // Can't re-parent this until after Mecanim setup, or else Mecanim gets confused.
        m_rootObject.transform.parent = this.StreamingClient.transform;
        m_rootObject.transform.localPosition = Vector3.zero;
        m_rootObject.transform.localRotation = Quaternion.identity;
    }


    void Update()
    {
        OptitrackSkeletonState skelState = StreamingClient.GetLatestSkeletonState(m_skeletonDef.Id);
        if (skelState != null)
        {
            // Update the transforms of the bone GameObjects.
            for (int i = 0; i < m_skeletonDef.Bones.Count; ++i)
            {
                Int32 boneId = m_skeletonDef.Bones[i].Id;

                OptitrackPose bonePose;
                GameObject boneObject;

                bool foundPose = false;
                if (StreamingClient.SkeletonCoordinates == StreamingCoordinatesValues.Global)
                {
                    // Use global skeleton coordinates
                    foundPose = skelState.LocalBonePoses.TryGetValue(boneId, out bonePose);
                }
                else
                {
                    // Use local skeleton coordinates
                    foundPose = skelState.BonePoses.TryGetValue(boneId, out bonePose);
                }

                bool foundObject = m_boneObjectMap.TryGetValue(boneId, out boneObject);
                if (foundPose && foundObject)
                {
                    boneObject.transform.localPosition = bonePose.Position;
                    boneObject.transform.localRotation = bonePose.Orientation;
                }

                string to_print = "boneId: " + boneId + " boneName: " + m_skeletonDef.Bones[i].Name + " objName: " + boneObject.name;
                Debug.Log(to_print, this);
            }

            // Perform Mecanim retargeting.
            if (m_srcPoseHandler != null && m_destPoseHandler != null)
            {
                // Interpret the streamed pose into Mecanim muscle space representation.
                m_srcPoseHandler.GetHumanPose(ref m_humanPose);

                // Re-target that muscle space pose to the destination avatar.
                m_destPoseHandler.SetHumanPose(ref m_humanPose);
            }
        }
    }

    #region Private methods
    /// <summary>
    /// Constructs the source Avatar and pose handlers for Mecanim retargeting.
    /// </summary>
    /// <param name="rootObjectName"></param>
    private void MecanimSetup(string rootObjectName)
    {
        string[] humanTraitBoneNames = HumanTrait.BoneName;

        // Set up the mapping between Mecanim human anatomy and OptiTrack skeleton representations.
        List<HumanBone> humanBones = new List<HumanBone>(m_skeletonDef.Bones.Count);
        for (int humanBoneNameIdx = 0; humanBoneNameIdx < humanTraitBoneNames.Length; ++humanBoneNameIdx)
        {
            string humanBoneName = humanTraitBoneNames[humanBoneNameIdx];
            if (m_cachedMecanimBoneNameMap.ContainsKey(humanBoneName))
            {
                HumanBone humanBone = new HumanBone();
                humanBone.humanName = humanBoneName;
                humanBone.boneName = m_cachedMecanimBoneNameMap[humanBoneName];
                humanBone.limit.useDefaultValues = true;

                humanBones.Add(humanBone);
            }
        }

        // Set up the T-pose and game object name mappings.
        List<SkeletonBone> skeletonBones = new List<SkeletonBone>(m_skeletonDef.Bones.Count + 1);

        // Special case: Create the root bone.
        {
            SkeletonBone rootBone = new SkeletonBone();
            rootBone.name = rootObjectName;
            rootBone.position = Vector3.zero;
            rootBone.rotation = Quaternion.identity;
            rootBone.scale = Vector3.one;

            skeletonBones.Add(rootBone);
        }

        // Create remaining re-targeted bone definitions.
        for (int boneDefIdx = 0; boneDefIdx < m_skeletonDef.Bones.Count; ++boneDefIdx)
        {
            OptitrackSkeletonDefinition.BoneDefinition boneDef = m_skeletonDef.Bones[boneDefIdx];

            SkeletonBone skelBone = new SkeletonBone();
            skelBone.name = boneDef.Name;
            skelBone.position = boneDef.Offset;
            skelBone.rotation = RemapBoneRotation(boneDef.Name); //Identity unless it's the thumb bone. 
            skelBone.scale = Vector3.one;

            skeletonBones.Add(skelBone);
        }

        // Now set up the HumanDescription for the retargeting source Avatar.
        HumanDescription humanDesc = new HumanDescription();
        humanDesc.human = humanBones.ToArray();
        humanDesc.skeleton = skeletonBones.ToArray();

        // These all correspond to default values.
        humanDesc.upperArmTwist = 0.5f;
        humanDesc.lowerArmTwist = 0.5f;
        humanDesc.upperLegTwist = 0.5f;
        humanDesc.lowerLegTwist = 0.5f;
        humanDesc.armStretch = 0.05f;
        humanDesc.legStretch = 0.05f;
        humanDesc.feetSpacing = 0.0f;
        humanDesc.hasTranslationDoF = false;

        // Finally, take the description and build the Avatar and pose handlers.
        m_srcAvatar = AvatarBuilder.BuildHumanAvatar(m_rootObject, humanDesc);

        if (m_srcAvatar.isValid == false || m_srcAvatar.isHuman == false)
        {
            Debug.LogError(GetType().FullName + ": Unable to create source Avatar for retargeting. Check that your Skeleton Asset Name and Bone Naming Convention are configured correctly.", this);
            this.enabled = false;
            return;
        }

        m_srcPoseHandler = new HumanPoseHandler(m_srcAvatar, m_rootObject.transform);
        m_destPoseHandler = new HumanPoseHandler(DestinationAvatar, this.transform);
    }


    /// <summary>
    /// Adjusts default position of the proximal thumb bones. 
    /// The default pose between Unity and Motive differs on the thumb, this fixes that difference. 
    /// </summary>
    /// <param name="boneName">The name of the current bone.</param>
    private Quaternion RemapBoneRotation(string boneName)
    {
        if (this.StreamingClient.BoneNamingConvention == OptitrackBoneNameConvention.Motive)
        {
            if (boneName.EndsWith("_LThumb1"))
            {
                // 60 Deg Y-Axis rotation
                return new Quaternion(0.0f, 0.5000011f, 0.0f, 0.8660248f);
            }
            if (boneName.EndsWith("_RThumb1"))
            {
                // -60 Deg Y-Axis rotation
                return new Quaternion(0.0f, -0.5000011f, 0.0f, 0.8660248f);
            }
        }
        if (this.StreamingClient.BoneNamingConvention == OptitrackBoneNameConvention.FBX)
        {
            if (boneName.EndsWith("_LeftHandThumb1"))
            {
                // 60 Deg Y-Axis rotation
                return new Quaternion(0.0f, 0.5000011f, 0.0f, 0.8660248f);
            }
            if (boneName.EndsWith("_RightHandThumb1"))
            {
                // -60 Deg Y-Axis rotation
                return new Quaternion(0.0f, -0.5000011f, 0.0f, 0.8660248f);
            }
        }
        if (this.StreamingClient.BoneNamingConvention == OptitrackBoneNameConvention.BVH)
        {
            if (boneName.EndsWith("_LeftFinger0"))
            {
                // 60 Deg Y-Axis rotation
                return new Quaternion(0.0f, 0.5000011f, 0.0f, 0.8660248f);
            }
            if (boneName.EndsWith("_RightFinger0"))
            {
                // -60 Deg Y-Axis rotation
                return new Quaternion(0.0f, -0.5000011f, 0.0f, 0.8660248f);
            }
        }

        return Quaternion.identity;
    }


    /// <summary>
    /// Updates the <see cref="m_cachedMecanimBoneNameMap"/> lookup to reflect the specified bone naming convention
    /// and source skeleton asset name.
    /// </summary>
    /// <param name="convention">The bone naming convention to use. Must match the host software.</param>
    /// <param name="assetName">The name of the source skeleton asset.</param>
    private void CacheBoneNameMap(OptitrackBoneNameConvention convention, string assetName)
    {
        m_cachedMecanimBoneNameMap.Clear();

        switch (convention)
        {
            case OptitrackBoneNameConvention.Motive:
                m_cachedMecanimBoneNameMap.Add("Hips", assetName + "_Hip");
                m_cachedMecanimBoneNameMap.Add("Spine", assetName + "_Ab");
                m_cachedMecanimBoneNameMap.Add("Chest", assetName + "_Chest");
                m_cachedMecanimBoneNameMap.Add("Neck", assetName + "_Neck");
                m_cachedMecanimBoneNameMap.Add("Head", assetName + "_Head");

                m_cachedMecanimBoneNameMap.Add("LeftShoulder", assetName + "_LShoulder");
                m_cachedMecanimBoneNameMap.Add("LeftUpperArm", assetName + "_LUArm");
                m_cachedMecanimBoneNameMap.Add("LeftLowerArm", assetName + "_LFArm");
                m_cachedMecanimBoneNameMap.Add("LeftHand", assetName + "_LHand");

                m_cachedMecanimBoneNameMap.Add("RightShoulder", assetName + "_RShoulder");
                m_cachedMecanimBoneNameMap.Add("RightUpperArm", assetName + "_RUArm");
                m_cachedMecanimBoneNameMap.Add("RightLowerArm", assetName + "_RFArm");
                m_cachedMecanimBoneNameMap.Add("RightHand", assetName + "_RHand");

                m_cachedMecanimBoneNameMap.Add("LeftUpperLeg", assetName + "_LThigh");
                m_cachedMecanimBoneNameMap.Add("LeftLowerLeg", assetName + "_LShin");
                m_cachedMecanimBoneNameMap.Add("LeftFoot", assetName + "_LFoot");
                m_cachedMecanimBoneNameMap.Add("LeftToeBase", assetName + "_LToe");

                m_cachedMecanimBoneNameMap.Add("RightUpperLeg", assetName + "_RThigh");
                m_cachedMecanimBoneNameMap.Add("RightLowerLeg", assetName + "_RShin");
                m_cachedMecanimBoneNameMap.Add("RightFoot", assetName + "_RFoot");
                m_cachedMecanimBoneNameMap.Add("RightToeBase", assetName + "_RToe");


                m_cachedMecanimBoneNameMap.Add("Left Thumb Proximal", assetName + "_LThumb1");
                m_cachedMecanimBoneNameMap.Add("Left Thumb Intermediate", assetName + "_LThumb2");
                m_cachedMecanimBoneNameMap.Add("Left Thumb Distal", assetName + "_LThumb3");
                m_cachedMecanimBoneNameMap.Add("Right Thumb Proximal", assetName + "_RThumb1");
                m_cachedMecanimBoneNameMap.Add("Right Thumb Intermediate", assetName + "_RThumb2");
                m_cachedMecanimBoneNameMap.Add("Right Thumb Distal", assetName + "_RThumb3");

                m_cachedMecanimBoneNameMap.Add("Left Index Proximal", assetName + "_LIndex1");
                m_cachedMecanimBoneNameMap.Add("Left Index Intermediate", assetName + "_LIndex2");
                m_cachedMecanimBoneNameMap.Add("Left Index Distal", assetName + "_LIndex3");
                m_cachedMecanimBoneNameMap.Add("Right Index Proximal", assetName + "_RIndex1");
                m_cachedMecanimBoneNameMap.Add("Right Index Intermediate", assetName + "_RIndex2");
                m_cachedMecanimBoneNameMap.Add("Right Index Distal", assetName + "_RIndex3");

                m_cachedMecanimBoneNameMap.Add("Left Middle Proximal", assetName + "_LMiddle1");
                m_cachedMecanimBoneNameMap.Add("Left Middle Intermediate", assetName + "_LMiddle2");
                m_cachedMecanimBoneNameMap.Add("Left Middle Distal", assetName + "_LMiddle3");
                m_cachedMecanimBoneNameMap.Add("Right Middle Proximal", assetName + "_RMiddle1");
                m_cachedMecanimBoneNameMap.Add("Right Middle Intermediate", assetName + "_RMiddle2");
                m_cachedMecanimBoneNameMap.Add("Right Middle Distal", assetName + "_RMiddle3");

                m_cachedMecanimBoneNameMap.Add("Left Ring Proximal", assetName + "_LRing1");
                m_cachedMecanimBoneNameMap.Add("Left Ring Intermediate", assetName + "_LRing2");
                m_cachedMecanimBoneNameMap.Add("Left Ring Distal", assetName + "_LRing3");
                m_cachedMecanimBoneNameMap.Add("Right Ring Proximal", assetName + "_RRing1");
                m_cachedMecanimBoneNameMap.Add("Right Ring Intermediate", assetName + "_RRing2");
                m_cachedMecanimBoneNameMap.Add("Right Ring Distal", assetName + "_RRing3");

                m_cachedMecanimBoneNameMap.Add("Left Little Proximal", assetName + "_LPinky1");
                m_cachedMecanimBoneNameMap.Add("Left Little Intermediate", assetName + "_LPinky2");
                m_cachedMecanimBoneNameMap.Add("Left Little Distal", assetName + "_LPinky3");
                m_cachedMecanimBoneNameMap.Add("Right Little Proximal", assetName + "_RPinky1");
                m_cachedMecanimBoneNameMap.Add("Right Little Intermediate", assetName + "_RPinky2");
                m_cachedMecanimBoneNameMap.Add("Right Little Distal", assetName + "_RPinky3");
                break;
            case OptitrackBoneNameConvention.FBX:
                m_cachedMecanimBoneNameMap.Add("Hips", assetName + "_Hips");
                m_cachedMecanimBoneNameMap.Add("Spine", assetName + "_Spine");
                m_cachedMecanimBoneNameMap.Add("Chest", assetName + "_Spine1");
                m_cachedMecanimBoneNameMap.Add("Neck", assetName + "_Neck");
                m_cachedMecanimBoneNameMap.Add("Head", assetName + "_Head");

                m_cachedMecanimBoneNameMap.Add("LeftShoulder", assetName + "_LeftShoulder");
                m_cachedMecanimBoneNameMap.Add("LeftUpperArm", assetName + "_LeftArm");
                m_cachedMecanimBoneNameMap.Add("LeftLowerArm", assetName + "_LeftForeArm");
                m_cachedMecanimBoneNameMap.Add("LeftHand", assetName + "_LeftHand");

                m_cachedMecanimBoneNameMap.Add("RightShoulder", assetName + "_RightShoulder");
                m_cachedMecanimBoneNameMap.Add("RightUpperArm", assetName + "_RightArm");
                m_cachedMecanimBoneNameMap.Add("RightLowerArm", assetName + "_RightForeArm");
                m_cachedMecanimBoneNameMap.Add("RightHand", assetName + "_RightHand");

                m_cachedMecanimBoneNameMap.Add("LeftUpperLeg", assetName + "_LeftUpLeg");
                m_cachedMecanimBoneNameMap.Add("LeftLowerLeg", assetName + "_LeftLeg");
                m_cachedMecanimBoneNameMap.Add("LeftFoot", assetName + "_LeftFoot");
                m_cachedMecanimBoneNameMap.Add("LeftToeBase", assetName + "_LeftToeBase");

                m_cachedMecanimBoneNameMap.Add("RightUpperLeg", assetName + "_RightUpLeg");
                m_cachedMecanimBoneNameMap.Add("RightLowerLeg", assetName + "_RightLeg");
                m_cachedMecanimBoneNameMap.Add("RightFoot", assetName + "_RightFoot");
                m_cachedMecanimBoneNameMap.Add("RightToeBase", assetName + "_RightToeBase");


                m_cachedMecanimBoneNameMap.Add("Left Thumb Proximal", assetName + "_LeftHandThumb1");
                m_cachedMecanimBoneNameMap.Add("Left Thumb Intermediate", assetName + "_LeftHandThumb2");
                m_cachedMecanimBoneNameMap.Add("Left Thumb Distal", assetName + "_LeftHandThumb3");
                m_cachedMecanimBoneNameMap.Add("Right Thumb Proximal", assetName + "_RightHandThumb1");
                m_cachedMecanimBoneNameMap.Add("Right Thumb Intermediate", assetName + "_RightHandThumb2");
                m_cachedMecanimBoneNameMap.Add("Right Thumb Distal", assetName + "_RightHandThumb3");

                m_cachedMecanimBoneNameMap.Add("Left Index Proximal", assetName + "_LeftHandIndex1");
                m_cachedMecanimBoneNameMap.Add("Left Index Intermediate", assetName + "_LeftHandIndex2");
                m_cachedMecanimBoneNameMap.Add("Left Index Distal", assetName + "_LeftHandIndex3");
                m_cachedMecanimBoneNameMap.Add("Right Index Proximal", assetName + "_RightHandIndex1");
                m_cachedMecanimBoneNameMap.Add("Right Index Intermediate", assetName + "_RightHandIndex2");
                m_cachedMecanimBoneNameMap.Add("Right Index Distal", assetName + "_RightHandIndex3");

                m_cachedMecanimBoneNameMap.Add("Left Middle Proximal", assetName + "_LeftHandMiddle1");
                m_cachedMecanimBoneNameMap.Add("Left Middle Intermediate", assetName + "_LeftHandMiddle2");
                m_cachedMecanimBoneNameMap.Add("Left Middle Distal", assetName + "_LeftHandMiddle3");
                m_cachedMecanimBoneNameMap.Add("Right Middle Proximal", assetName + "_RightHandMiddle1");
                m_cachedMecanimBoneNameMap.Add("Right Middle Intermediate", assetName + "_RightHandMiddle2");
                m_cachedMecanimBoneNameMap.Add("Right Middle Distal", assetName + "_RightHandMiddle3");

                m_cachedMecanimBoneNameMap.Add("Left Ring Proximal", assetName + "_LeftHandRing1");
                m_cachedMecanimBoneNameMap.Add("Left Ring Intermediate", assetName + "_LeftHandRing2");
                m_cachedMecanimBoneNameMap.Add("Left Ring Distal", assetName + "_LeftHandRing3");
                m_cachedMecanimBoneNameMap.Add("Right Ring Proximal", assetName + "_RightHandRing1");
                m_cachedMecanimBoneNameMap.Add("Right Ring Intermediate", assetName + "_RightHandRing2");
                m_cachedMecanimBoneNameMap.Add("Right Ring Distal", assetName + "_RightHandRing3");

                m_cachedMecanimBoneNameMap.Add("Left Little Proximal", assetName + "_LeftHandPinky1");
                m_cachedMecanimBoneNameMap.Add("Left Little Intermediate", assetName + "_LeftHandPinky2");
                m_cachedMecanimBoneNameMap.Add("Left Little Distal", assetName + "_LeftHandPinky3");
                m_cachedMecanimBoneNameMap.Add("Right Little Proximal", assetName + "_RightHandPinky1");
                m_cachedMecanimBoneNameMap.Add("Right Little Intermediate", assetName + "_RightHandPinky2");
                m_cachedMecanimBoneNameMap.Add("Right Little Distal", assetName + "_RightHandPinky3");
                break;
            case OptitrackBoneNameConvention.BVH:
                m_cachedMecanimBoneNameMap.Add("Hips", assetName + "_Hips");
                m_cachedMecanimBoneNameMap.Add("Spine", assetName + "_Chest");
                m_cachedMecanimBoneNameMap.Add("Chest", assetName + "_Chest2");
                m_cachedMecanimBoneNameMap.Add("Neck", assetName + "_Neck");
                m_cachedMecanimBoneNameMap.Add("Head", assetName + "_Head");

                m_cachedMecanimBoneNameMap.Add("LeftShoulder", assetName + "_LeftCollar");
                m_cachedMecanimBoneNameMap.Add("LeftUpperArm", assetName + "_LeftShoulder");
                m_cachedMecanimBoneNameMap.Add("LeftLowerArm", assetName + "_LeftElbow");
                m_cachedMecanimBoneNameMap.Add("LeftHand", assetName + "_LeftWrist");

                m_cachedMecanimBoneNameMap.Add("RightShoulder", assetName + "_RightCollar");
                m_cachedMecanimBoneNameMap.Add("RightUpperArm", assetName + "_RightShoulder");
                m_cachedMecanimBoneNameMap.Add("RightLowerArm", assetName + "_RightElbow");
                m_cachedMecanimBoneNameMap.Add("RightHand", assetName + "_RightWrist");

                m_cachedMecanimBoneNameMap.Add("LeftUpperLeg", assetName + "_LeftHip");
                m_cachedMecanimBoneNameMap.Add("LeftLowerLeg", assetName + "_LeftKnee");
                m_cachedMecanimBoneNameMap.Add("LeftFoot", assetName + "_LeftAnkle");
                m_cachedMecanimBoneNameMap.Add("LeftToeBase", assetName + "_LeftToe");

                m_cachedMecanimBoneNameMap.Add("RightUpperLeg", assetName + "_RightHip");
                m_cachedMecanimBoneNameMap.Add("RightLowerLeg", assetName + "_RightKnee");
                m_cachedMecanimBoneNameMap.Add("RightFoot", assetName + "_RightAnkle");
                m_cachedMecanimBoneNameMap.Add("RightToeBase", assetName + "_RightToe");


                m_cachedMecanimBoneNameMap.Add("Left Thumb Proximal", assetName + "_LeftFinger0");
                m_cachedMecanimBoneNameMap.Add("Left Thumb Intermediate", assetName + "_LeftFinger01");
                m_cachedMecanimBoneNameMap.Add("Left Thumb Distal", assetName + "_LeftFinger02");
                m_cachedMecanimBoneNameMap.Add("Right Thumb Proximal", assetName + "_RightFinger0");
                m_cachedMecanimBoneNameMap.Add("Right Thumb Intermediate", assetName + "_RightFinger01");
                m_cachedMecanimBoneNameMap.Add("Right Thumb Distal", assetName + "_RightFinger02");

                m_cachedMecanimBoneNameMap.Add("Left Index Proximal", assetName + "_LeftFinger1");
                m_cachedMecanimBoneNameMap.Add("Left Index Intermediate", assetName + "_LeftFinger11");
                m_cachedMecanimBoneNameMap.Add("Left Index Distal", assetName + "_LeftFinger12");
                m_cachedMecanimBoneNameMap.Add("Right Index Proximal", assetName + "_RightFinger1");
                m_cachedMecanimBoneNameMap.Add("Right Index Intermediate", assetName + "_RightFinger11");
                m_cachedMecanimBoneNameMap.Add("Right Index Distal", assetName + "_RightFinger12");

                m_cachedMecanimBoneNameMap.Add("Left Middle Proximal", assetName + "_LeftFinger2");
                m_cachedMecanimBoneNameMap.Add("Left Middle Intermediate", assetName + "_LeftFinger21");
                m_cachedMecanimBoneNameMap.Add("Left Middle Distal", assetName + "_LeftFinger22");
                m_cachedMecanimBoneNameMap.Add("Right Middle Proximal", assetName + "_RightFinger2");
                m_cachedMecanimBoneNameMap.Add("Right Middle Intermediate", assetName + "_RightFinger21");
                m_cachedMecanimBoneNameMap.Add("Right Middle Distal", assetName + "_RightFinger22");

                m_cachedMecanimBoneNameMap.Add("Left Ring Proximal", assetName + "_LeftFinger3");
                m_cachedMecanimBoneNameMap.Add("Left Ring Intermediate", assetName + "_LeftFinger31");
                m_cachedMecanimBoneNameMap.Add("Left Ring Distal", assetName + "_LeftFinger32");
                m_cachedMecanimBoneNameMap.Add("Right Ring Proximal", assetName + "_RightFinger3");
                m_cachedMecanimBoneNameMap.Add("Right Ring Intermediate", assetName + "_RightFinger31");
                m_cachedMecanimBoneNameMap.Add("Right Ring Distal", assetName + "_RightFinger32");

                m_cachedMecanimBoneNameMap.Add("Left Little Proximal", assetName + "_LeftFinger4");
                m_cachedMecanimBoneNameMap.Add("Left Little Intermediate", assetName + "_LeftFinger41");
                m_cachedMecanimBoneNameMap.Add("Left Little Distal", assetName + "_LeftFinger42");
                m_cachedMecanimBoneNameMap.Add("Right Little Proximal", assetName + "_RightFinger4");
                m_cachedMecanimBoneNameMap.Add("Right Little Intermediate", assetName + "_RightFinger41");
                m_cachedMecanimBoneNameMap.Add("Right Little Distal", assetName + "_RightFinger42");
                break;
        }
    }
    #endregion Private methods
}
