using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Unity.Collections;

/// <progress>
/// Made substantial progress, now need to perfect the per avatar correspondance of the actual transforms to HumanBodyBones
/// The Humandescription skeleton crosschecked to see which bones have also human bones seem to be the correct route
/// Need to explore animator and avatar class to see if there are extra clues. 
/// </progress>

[RequireComponent(typeof(Animator))]
public class OptitrackPosePlayable : MonoBehaviour
{
    //PlayableGraph Stuff
    private Animator animator;
    private PlayableGraph graph;
    private Avatar avatar;
    private Dictionary<Int32, int> optitrack2mecanim; 

    //Optitrack Stuff
    [Header("Optitrack Stuff")]
    public PlayableOptitrackStreamingClient client;
    public string skeleton_name;
    public bool connectBones = true;
    public bool showBonesReferenceFrame = false;

    [Header("Retargeting Stuff")]
    public Transform destination_root;

    [Header("Modify Quaternion Stuff")]
    public bool update_behaviours = true;
    public HumanBodyBones hbb_index;
    public int int_index;
    public Quaternion modify_quaternion;

    private GameObject m_rootObject;
    private OptitrackSkeletonDefinition m_skeletonDef;
    private Dictionary<string, string> m_cachedMecanimBoneNameMap = new Dictionary<string, string>();
    private Dictionary<Int32, GameObject> m_boneObjectMap;
    private Avatar m_srcAvatar;
    private Animator optitrackAvatarAnimator;

    //OPTITRACK POSE PLAYABLE
    private ScriptPlayableOutput output;
    private ScriptPlayable<OptitrackPoseBehaviour> posePlayable;
    private OptitrackPoseBehaviour behaviour;

    //AVATAR T-POSE BEHAVIOUR
    private ScriptPlayable<AvatarTPoseBehaviour> tposePlayable;
    private AvatarTPoseBehaviour tposeBehaviour;

    //OUTPUT 1 - OPTITRACK
    private AnimationPlayableOutput optitrakPlayableOutput;
    private PoseApplyJob poseApplyJob;
    private AnimationScriptPlayable animationPlayable;

    //OUTPUT 2 - AVATAR
    private AnimationPlayableOutput avatarPlayableOutput;
    private PoseApplyJob poseApplyJob2;
    private AnimationScriptPlayable animationPlayable2;

    //AVATAR RETARGETER
    private ScriptPlayable<AvatarRetargetingBehaviour2> retargetingPlayable;
    private AvatarRetargetingBehaviour2 retargetingBehaviour;

    //QUATERNION MODIFICATION TARGET
    private ScriptPlayable<QuaternionHandler> quatPlayable;
    private QuaternionHandler quatBehaviour;
    private ScriptPlayable<PoseConjunction> junctionPlayable;
    private PoseConjunction junctionBehaviour;

    //QUATERNION MODIFICATION OPTITRACK
    private ScriptPlayable<QuaternionHandler> quatPlayable2;
    private QuaternionHandler quatBehaviour2;
    private ScriptPlayable<PoseConjunction> junctionPlayable2;
    private PoseConjunction junctionBehaviour2;

    /// <summary>
    /// Process:
    ///     - Create the graph
    ///     - Create playable
    ///     - Setup Playables
    ///     - Connect nodes
    /// </summary>

    // Start is called before the first frame update
    void Start()
    {
        animator = GetComponent<Animator>();
        avatar = animator.avatar;

        SetupOptitrack();
        //FillMirrors();

        graph = PlayableGraph.Create("Optitrack Test_" + UnityEngine.Random.Range(0.0f, 1.0f));
        
        output = ScriptPlayableOutput.Create(graph, "output");
        optitrakPlayableOutput = AnimationPlayableOutput.Create(graph, "Optitrack Animation Output", optitrackAvatarAnimator);
        avatarPlayableOutput   = AnimationPlayableOutput.Create(graph, "Avatar Animation Output", animator);
        PlayableOutputExtensions.SetUserData(output, this);

        poseApplyJob = new PoseApplyJob();
        poseApplyJob2 = new PoseApplyJob();
        
        posePlayable = ScriptPlayable<OptitrackPoseBehaviour>.Create(graph);
        tposePlayable = ScriptPlayable<AvatarTPoseBehaviour>.Create(graph);
        quatPlayable = ScriptPlayable<QuaternionHandler>.Create(graph);
        junctionPlayable = ScriptPlayable<PoseConjunction>.Create(graph);
        quatPlayable2 = ScriptPlayable<QuaternionHandler>.Create(graph);
        junctionPlayable2 = ScriptPlayable<PoseConjunction>.Create(graph);
        retargetingPlayable = ScriptPlayable<AvatarRetargetingBehaviour2>.Create(graph);
        behaviour = posePlayable.GetBehaviour();
        tposeBehaviour = tposePlayable.GetBehaviour();
        quatBehaviour = quatPlayable.GetBehaviour();
        junctionBehaviour = junctionPlayable.GetBehaviour();
        quatBehaviour2 = quatPlayable2.GetBehaviour();
        junctionBehaviour2 = junctionPlayable2.GetBehaviour();
        retargetingBehaviour = retargetingPlayable.GetBehaviour();

        FillLink(m_boneObjectMap, optitrackAvatarAnimator);

        poseApplyJob.Init(posePlayable.GetBehaviour(), optitrackAvatarAnimator, true);
        poseApplyJob2.Init(retargetingPlayable.GetBehaviour(), animator, true);
        behaviour.OptitrackSetup(client, m_skeletonDef, MecanimHumanoidExtension.OptitrackId2HumanBodyBones(m_boneObjectMap, optitrackAvatarAnimator));
        tposeBehaviour.TPoseSetup(optitrackAvatarAnimator);
        retargetingBehaviour.RetargetingSetup(optitrackAvatarAnimator, m_rootObject.transform, animator, destination_root, posePlayable.GetBehaviour());
        quatBehaviour.QuaternionSetup((int)hbb_index, retargetingBehaviour, modify_quaternion);
        junctionBehaviour.SetupConjunction(retargetingBehaviour, (int)hbb_index, quatBehaviour);
        quatBehaviour2.QuaternionSetup((int)hbb_index, tposePlayable.GetBehaviour(), modify_quaternion);
        junctionBehaviour2.SetupConjunction(tposePlayable.GetBehaviour(), (int)hbb_index, quatPlayable2.GetBehaviour());

        animationPlayable = AnimationScriptPlayable.Create(graph, poseApplyJob);
        animationPlayable2 = AnimationScriptPlayable.Create(graph, poseApplyJob2);

        //AnimationGraphUtility.ConnectNodes(graph, posePlayable, junctionPlayable2);
        //AnimationGraphUtility.ConnectNodes(graph, posePlayable, quatPlayable2);
        //AnimationGraphUtility.ConnectNodes(graph, quatPlayable2, junctionPlayable2);
        AnimationGraphUtility.ConnectNodes(graph, posePlayable, animationPlayable);

        AnimationGraphUtility.ConnectNodes(graph, posePlayable, retargetingPlayable);
        AnimationGraphUtility.ConnectNodes(graph, retargetingPlayable, animationPlayable2);

        AnimationGraphUtility.ConnectOutput(posePlayable, output);   
        AnimationGraphUtility.ConnectOutput(animationPlayable, optitrakPlayableOutput);
        AnimationGraphUtility.ConnectOutput(animationPlayable2, avatarPlayableOutput);

        graph.Play();
    }

    private void Update()
    {
        int_index = (int)hbb_index;
        if (!update_behaviours) { return; }
        quatBehaviour.UpdateData((int)hbb_index, modify_quaternion);
        junctionBehaviour.UpdateData((int)hbb_index, quatBehaviour);
        quatBehaviour2.UpdateData((int)hbb_index, modify_quaternion);
        junctionBehaviour2.UpdateData((int)hbb_index, quatBehaviour2);

        //FillMirrors();
        //retargetingBehaviour.UpdateMirrors(mirrorList, mirrorAxis);
    }

    /*
    private void FillMirrors()
    {
        mirrorList = new List<bool>((int)HumanBodyBones.LastBone);
        mirrorAxis = new List<Vector3>((int)HumanBodyBones.LastBone);

        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            mirrorList.Add(false);
            mirrorAxis.Add(Vector3.zero);
        }

        foreach (Vector4 info in mirrors)
        {
            mirrorList[(int)info.x] = true;
            mirrorAxis[(int)info.x] = new Vector3(info.y, info.z, info.w);
        }
    }*/

    private void TestCorrepondence()
    {
        /*
        Dictionary<int, int> translation = MecanimHumanoidExtension.OptitrackId2HumanBodyBones(m_boneObjectMap, optitrackAvatarAnimator);

        foreach ((int OptitrackID, int HBBindex) in translation)
        {
            string optitrack_name = m_boneObjectMap[OptitrackID].name;
            string animator_name = optitrackAvatarAnimator.GetBoneTransform((HumanBodyBones)HBBindex).name;
            Debug.Log("OptitrackID [" + OptitrackID + "," + optitrack_name + "] HBBIndex [" + HBBindex + "," + animator_name + "]");
        }
        */

        string to_print = "HumanDescription: \n";
        HumanDescription hd = optitrackAvatarAnimator.avatar.humanDescription;

        for (int i = 0; i < hd.skeleton.Length; i++)
        {
            to_print += hd.skeleton[i].name + "\n";
        }

        Debug.Log(to_print, this);

        to_print = "HumanBodyBones: \n";
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            if (optitrackAvatarAnimator.GetBoneTransform((HumanBodyBones)i))
            {
                to_print += System.Enum.GetName(typeof(HumanBodyBones), i) + "\n";
            }
        }
        Debug.Log(to_print, this);
    }

    private void SetupOptitrack()
    {
        // If the user didn't explicitly associate a client, find a suitable default.
        if (this.client == null)
        {
            this.client = PlayableOptitrackStreamingClient.FindDefaultClient();

            // If we still couldn't find one, disable this component.
            if (this.client == null)
            {
                Debug.LogError(GetType().FullName + ": Streaming client not set, and no " + typeof(PlayableOptitrackStreamingClient).FullName + " components found in scene; disabling this component.", this);
                this.enabled = false;
                return;
            }
        }

        this.client.RegisterSkeleton(this.skeleton_name);

        // Create a lookup from Mecanim anatomy bone names to OptiTrack streaming bone names.
        CacheBoneNameMap(this.client.BoneNamingConvention, this.skeleton_name);

        // Retrieve the OptiTrack skeleton definition.
        m_skeletonDef = this.client.GetSkeletonDefinitionByName(this.skeleton_name);

        if (m_skeletonDef == null)
        {
            Debug.LogError(GetType().FullName + ": Could not find skeleton definition with the name \"" + this.skeleton_name + "\"", this);
            this.enabled = false;
            return;
        }

        // Create a hierarchy of GameObjects that will receive the skeletal pose data.
        string rootObjectName = "OptiTrack Skeleton - " + skeleton_name;
        m_rootObject = new GameObject(rootObjectName);
        //root_for_job = m_rootObject.transform;

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

            if (showBonesReferenceFrame)
            {
                DebugDisplayDirections ddd = boneObject.AddComponent<DebugDisplayDirections>();
                ddd.length = 0.1f;
            }
        }

        // Hook up retargeting between those GameObjects and the destination Avatar.
        MecanimSetup(rootObjectName);

        // Can't re-parent this until after Mecanim setup, or else Mecanim gets confused.
        m_rootObject.transform.parent = this.client.transform;
        m_rootObject.transform.localPosition = Vector3.zero;
        m_rootObject.transform.localRotation = Quaternion.identity;

        //SETUP SECOND ANIMATOR -----------------------------------------------------------------------------
        optitrackAvatarAnimator = m_rootObject.AddComponent<Animator>();
        optitrackAvatarAnimator.avatar = m_srcAvatar;   
        //---------------------------------------------------------------------------------------------------
    }

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
                //Modified from "LeftToeBase"
                m_cachedMecanimBoneNameMap.Add("LeftToes", assetName + "_LToe");

                m_cachedMecanimBoneNameMap.Add("RightUpperLeg", assetName + "_RThigh");
                m_cachedMecanimBoneNameMap.Add("RightLowerLeg", assetName + "_RShin");
                m_cachedMecanimBoneNameMap.Add("RightFoot", assetName + "_RFoot");
                //Modified from "RightToeBase
                m_cachedMecanimBoneNameMap.Add("RightToes", assetName + "_RToe");


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
        m_srcAvatar.name = "Optitrack Source Avatar";

        if (m_srcAvatar.isValid == false || m_srcAvatar.isHuman == false)
        {
            Debug.LogError(GetType().FullName + ": Unable to create source Avatar for retargeting. Check that your Skeleton Asset Name and Bone Naming Convention are configured correctly.");
            return;
        }
    }

    private Quaternion RemapBoneRotation(string boneName)
    {
        if (client.BoneNamingConvention == OptitrackBoneNameConvention.Motive)
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
        if (client.BoneNamingConvention == OptitrackBoneNameConvention.FBX)
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
        if (client.BoneNamingConvention == OptitrackBoneNameConvention.BVH)
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

    private void FillLink(Dictionary<Int32, GameObject> dictionary, Animator animator)
    {
        optitrack2mecanim = new Dictionary<int, int>();
        foreach ((Int32 id, GameObject obj) in dictionary)
        {
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                if (obj.transform == animator.GetBoneTransform((HumanBodyBones)i))
                {
                    optitrack2mecanim[id] = i;
                }
            }
        }
    }

    private void OnDisable()
    {
        poseApplyJob2.Dispose();
        poseApplyJob.Dispose();
        behaviour.Dispose();
        retargetingBehaviour.Dispose();
        tposeBehaviour.Dispose();
        if (graph.IsValid())
        {
            graph.Stop();
            graph.Destroy();
        }
    }
}
