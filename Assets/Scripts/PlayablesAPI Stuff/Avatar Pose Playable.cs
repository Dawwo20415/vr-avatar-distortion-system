using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Playables;

public interface IHumanBodyBonesSplit
{
    public Quaternion GetRotation(int hbb_index);
    public Vector3 GetPosition(int hbb_index);
    public bool GetBoneStatus(int hbb_index);
}

public interface IInputableNode
{
    public bool ConnectInput(Playable behaviour);
}

public class GenericBehaviour : PlayableBehaviour
{
    public GenericBehaviour() { }
    public override void PrepareFrame(Playable playable, FrameData info) { }
    public override void ProcessFrame(Playable playable, FrameData info, object playerData) { }
}

public class AvatarPoseBehaviour : PlayableBehaviour, IHumanBodyBonesSplit
{
    protected NativeArray<Quaternion> source_avatar_bones;
    protected NativeArray<Vector3> source_avatar_positions;
    protected Dictionary<int, int> HBB2Index;
    protected Dictionary<int, bool> HBB2Available;

    public AvatarPoseBehaviour()
    {
        int size = (int)HumanBodyBones.LastBone;
        source_avatar_bones = new NativeArray<Quaternion>(size, Allocator.Persistent);
        source_avatar_positions = new NativeArray<Vector3>(size, Allocator.Persistent);
        HBB2Index = new Dictionary<int, int>(size);
        HBB2Available = new Dictionary<int, bool>(size);

        for (int i = 0; i < size; i++)
        {
            source_avatar_bones[i] = Quaternion.identity;
            source_avatar_positions[i] = Vector3.zero;
            HBB2Index[i] = i;
            HBB2Available[i] = false;
        }
    }

    public Quaternion GetRotation(int hbb_index)
    {
        //Debug.Log("Sending Rotation for index[" + hbb_index + "]");
        return source_avatar_bones[HBB2Index[hbb_index]];
    }

    public Vector3 GetPosition(int hbb_index)
    {
        return source_avatar_positions[HBB2Index[hbb_index]];
    }

    public bool GetBoneStatus(int hbb_index)
    {
        return HBB2Available[HBB2Index[hbb_index]];
    }

    public override void PrepareFrame(Playable playable, FrameData info) { }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData) { }

    public void Dispose()
    {
        source_avatar_bones.Dispose();
        source_avatar_positions.Dispose();
    }
}

public class AvatarRetargetingBehaviour2 : PlayableBehaviour, IHumanBodyBonesSplit
{
    private NativeArray<AvatarRetargetingComponents> components;
    private IHumanBodyBonesSplit behaviour;

    public void RetargetingSetup(Animator source_animator, Transform src_root, Animator destination_animator, Transform dest_root, IHumanBodyBonesSplit input_behaviour)
    {
        components = new NativeArray<AvatarRetargetingComponents>((int)HumanBodyBones.LastBone, Allocator.Persistent);

        Dictionary<int, int> source_hbb = MecanimHumanoidExtension.HumanBodyBones2AvatarSkeleton(source_animator);
        Dictionary<int, int> dest_hbb = MecanimHumanoidExtension.HumanBodyBones2AvatarSkeleton(destination_animator);

        behaviour = input_behaviour;

        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            if (source_hbb[i] == -1 || dest_hbb[i] == -1)
            {
                components[i] = AvatarRetargetingComponents.identity;
            } else
            {
                components[i] = FormComponents(source_animator, (HumanBodyBones)i, src_root, destination_animator, (HumanBodyBones)i, dest_root);
                if (i == 12 && false)
                {
                    Debug.Log("#" + i + " Original Retargeting Component" + " (" + ((HumanBodyBones)i).ToString() + ") -> " + components[i].ToString());
                    Debug.Log("#" + i + " Original Retargeting Component" + " (" + ((HumanBodyBones)i).ToString() + ") -> " + components[i].ToStringExtended());
                }
            }
        }
    }

    public Vector3 GetPosition(int hbb_index)
    {
        //return behaviour.GetPosition(hbb_index) + position_offsets[hbb_index];
        return behaviour.GetPosition(hbb_index);
    }

    public Quaternion GetRotation(int hbb_index)
    {
        Quaternion a = behaviour.GetRotation(hbb_index);
        Quaternion b = QExtension.ChangeFrame(Quaternion.Inverse(components[hbb_index].localA) * a, components[hbb_index].fromAtoB);

        return components[hbb_index].localB * b;
    }

    public bool GetBoneStatus(int hbb_index)
    {
        return behaviour.GetBoneStatus(hbb_index);
    }

    public override void PrepareFrame(Playable playable, FrameData info) { }
    public override void ProcessFrame(Playable playable, FrameData info, object playerData) { }

    public void Dispose()
    {
        components.Dispose();
    }

    private AvatarRetargetingComponents FormComponents(Animator src_anim, HumanBodyBones src, Transform src_root, Animator dest_anim, HumanBodyBones dest, Transform dest_root)
    {
        Quaternion src_local = GetBoneFromTransform(src_anim.avatar.humanDescription, src_anim.GetBoneTransform(src)).rotation;
        Quaternion dest_local = GetBoneFromTransform(dest_anim.avatar.humanDescription, dest_anim.GetBoneTransform(dest)).rotation;

        Quaternion fromRootToSrc = StackToParentAnimator(src_anim, src, src_root);
        Quaternion fromRootToDest = StackToParentAnimator(dest_anim, dest, dest_root);

        Quaternion fromSrctoDest = QExtension.FromTo(fromRootToSrc, fromRootToDest);

        //Debug.Log("SourceLocal " + QExtension.PrintEuler(src_local) + " | Destination Local " + QExtension.PrintEuler(dest_local) + " | From Src to Dest " + QExtension.PrintEuler(fromSrctoDest));
        //Debug.Log("SourceLocal " + QExtension.Print(src_local) + " | Destination Local " + QExtension.Print(dest_local) + " | From Src to Dest " + QExtension.Print(fromSrctoDest));

        return new AvatarRetargetingComponents(src_local, dest_local, fromSrctoDest);
    }

    private Quaternion StackToParentAnimator(Animator anim, HumanBodyBones index, Transform root)
    {
        Transform bone = anim.GetBoneTransform(index);
        Quaternion diff = Quaternion.identity;

        do
        {
            Quaternion tpose = GetBoneFromTransform(anim.avatar.humanDescription, bone).rotation;
            diff = tpose * diff;
            bone = bone.parent;
        } while (bone != root);

        return diff;
    }
    private SkeletonBone GetBoneFromTransform(HumanDescription hd, Transform trn)
    {
        for (int i = 0; i < hd.skeleton.Length; i++)
        {
            if (hd.skeleton[i].name == trn.name)
            {
                //Debug.Log("Skeleton Name " + hd.skeleton[i].name + " Transform name " + trn.name + " TPose quaternion " + QExtension.PrintEuler(hd.skeleton[i].rotation));
                return hd.skeleton[i];
            }
        }
        Debug.Log("Have not found the bone");
        return new SkeletonBone();
    }
}

public class AvatarRetargetingBehaviour : PlayableBehaviour, IHumanBodyBonesSplit
{
    private NativeArray<Quaternion> rotation_offsets;
    private NativeArray<Vector3> position_offsets;
    private NativeArray<bool> mirrored;
    private NativeArray<Vector3> mirror_axis;
    //private NativeArray<AvatarRetargetingComponents> components;
    //Input
    private IHumanBodyBonesSplit behaviour;

    public void RetargetingSetup(Animator source_animator, Animator destination_animator, IHumanBodyBonesSplit input_behaviour, List<bool> mirrorList, List<Vector3> mirrorAxis)
    {
        rotation_offsets = new NativeArray<Quaternion>((int)HumanBodyBones.LastBone, Allocator.Persistent);
        position_offsets = new NativeArray<Vector3>((int)HumanBodyBones.LastBone, Allocator.Persistent);
        mirrored = new NativeArray<bool>((int)HumanBodyBones.LastBone, Allocator.Persistent);
        mirror_axis = new NativeArray<Vector3>((int)HumanBodyBones.LastBone, Allocator.Persistent);

        Dictionary<int, int> source_hbb = MecanimHumanoidExtension.HumanBodyBones2AvatarSkeleton(source_animator);
        Dictionary<int, int> dest_hbb = MecanimHumanoidExtension.HumanBodyBones2AvatarSkeleton(destination_animator);

        if (mirrorList.Count != (int)HumanBodyBones.LastBone) { Debug.Log("Mirror List doesn not define all bones."); return; }
        if (mirrorAxis.Count != (int)HumanBodyBones.LastBone) { Debug.Log("Mirror Axis List doesn not define all bones."); return; }

        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            mirrored[i] = mirrorList[i];
            mirror_axis[i] = mirrorAxis[i];
            if (source_hbb[i] == -1 || dest_hbb[i] == -1)
            {
                rotation_offsets[i] = Quaternion.identity;
                position_offsets[i] = Vector3.zero;
            } else
            {
                Quaternion source_tpose = source_animator.avatar.humanDescription.skeleton[source_hbb[i]].rotation;
                Quaternion dest_tpose = destination_animator.avatar.humanDescription.skeleton[dest_hbb[i]].rotation;
                //if (mirrorList[i]) { Mirror(dest_tpose, mirrorAxis[i]); }

                Vector3 source_pos = source_animator.avatar.humanDescription.skeleton[source_hbb[i]].position;
                Vector3 dest_pos = destination_animator.avatar.humanDescription.skeleton[dest_hbb[i]].position;

                rotation_offsets[i] = dest_tpose * Quaternion.Inverse(source_tpose);
                position_offsets[i] = dest_pos - source_pos;
            }

        }

        behaviour = input_behaviour;
    }

    public void UpdateMirrors(List<bool> mirrorList, List<Vector3> mirrorAxis)
    {
        if (mirrorList.Count != (int)HumanBodyBones.LastBone) { Debug.Log("Mirror List doesn not define all bones."); return; }
        if (mirrorAxis.Count != (int)HumanBodyBones.LastBone) { Debug.Log("Mirror Axis List doesn not define all bones."); return; }

        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            mirrored[i] = mirrorList[i];
            mirror_axis[i] = mirrorAxis[i];
        }
    }

    public bool Connect(IHumanBodyBonesSplit input_behaviour)
    {
        behaviour = input_behaviour;
        return true;
    }

    public Vector3 GetPosition(int hbb_index)
    {
        //return behaviour.GetPosition(hbb_index) + position_offsets[hbb_index];
        return behaviour.GetPosition(hbb_index);
    }

    public Quaternion GetRotation(int hbb_index)
    {
        Quaternion newRot = behaviour.GetRotation(hbb_index);

        if (mirrored[hbb_index])
        {
            newRot = Mirror(newRot, mirror_axis[hbb_index]);
        }

        //return rotation_offsets[hbb_index] * newRot;
        return newRot * rotation_offsets[hbb_index];
    }

    public bool GetBoneStatus(int hbb_index)
    {
        return behaviour.GetBoneStatus(hbb_index);
    }

    public override void PrepareFrame(Playable playable, FrameData info) { }
    public override void ProcessFrame(Playable playable, FrameData info, object playerData) { }

    private Quaternion MirrorX(Quaternion q)
    {
        return new Quaternion(q.x, -q.y, -q.z, q.w);
    }

    private Quaternion MirrorY(Quaternion q)
    {
        return new Quaternion(-q.x, q.y, -q.z, q.w);
    }

    private Quaternion MirrorZ(Quaternion q)
    {
        return new Quaternion(-q.x, -q.y, q.z, q.w);
    }

    private Quaternion Mirror(Quaternion q, Vector3 axis)
    {
        Quaternion newRot = q;

        if (axis.x != 0) { newRot = MirrorX(newRot); }
        if (axis.y != 0) { newRot = MirrorY(newRot); }
        if (axis.z != 0) { newRot = MirrorZ(newRot); }

        return newRot;
    }

    public void Dispose()
    {
        rotation_offsets.Dispose();
        position_offsets.Dispose();
        mirrored.Dispose();
        mirror_axis.Dispose();
    }
}

public class AvatarTPoseBehaviour : AvatarPoseBehaviour
{
    public void TPoseSetup(Animator animator)
    {
        Dictionary<int, int> tmp = MecanimHumanoidExtension.AvatarSkeleton2HumanBodyBones(animator.avatar.humanDescription, animator);
        //Debug.Log("Using Animator with avatar [" + animator.avatar.name + "]");

        foreach((int skeleton_index, int HBB_index) in tmp)
        {
            if (HBB_index == -1) { continue; }
            source_avatar_bones[HBB_index] = animator.avatar.humanDescription.skeleton[skeleton_index].rotation;
            source_avatar_positions[HBB_index] = animator.avatar.humanDescription.skeleton[skeleton_index].position;
        }
    }
    
    public override void PrepareFrame(Playable playable, FrameData info) { }
    public override void ProcessFrame(Playable playable, FrameData info, object playerData) { }

}

public class OptitrackPoseBehaviour : AvatarPoseBehaviour
{
    private PlayableOptitrackStreamingClient client;
    private OptitrackSkeletonDefinition skeleton_definition;
    private Dictionary<Int32, int> id2HumanBodyBones;

    public void OptitrackSetup(PlayableOptitrackStreamingClient streamingClient, OptitrackSkeletonDefinition skeletonDefinition, Dictionary<Int32, int> correspondence)
    {
        client = streamingClient;
        skeleton_definition = skeletonDefinition; 
        id2HumanBodyBones = correspondence;

        foreach ((int key, int value) in id2HumanBodyBones)
        {
            HBB2Available[value] = true;
        }
    }

    public override void PrepareFrame(Playable playable, FrameData info)
    {
        //string to_print = "PrepareFrame" + " | StreamingClient_name:[" + client.name + "] StreamingClient_address[" + client.LocalAddress + "]";
        //Debug.Log(to_print);

        OptitrackSkeletonState skelState = client.GetLatestSkeletonState(skeleton_definition.Id);
        if (skelState != null)
        {
            // Update the transforms of the bone GameObjects.
            for (int i = 0; i < skeleton_definition.Bones.Count; ++i)
            {
                Int32 boneId = skeleton_definition.Bones[i].Id;

                OptitrackPose bonePose;

                bool foundPose = false;
                if (client.SkeletonCoordinates == StreamingCoordinatesValues.Global)
                {
                    // Use global skeleton coordinates
                    foundPose = skelState.LocalBonePoses.TryGetValue(boneId, out bonePose);
                }
                else
                {
                    // Use local skeleton coordinates
                    foundPose = skelState.BonePoses.TryGetValue(boneId, out bonePose);
                }

                if (foundPose)
                {
                    int index = id2HumanBodyBones[boneId];
                    source_avatar_bones[index] = bonePose.Orientation;
                    source_avatar_positions[index] = bonePose.Position;
                }
            }
        }
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData) { }
}
