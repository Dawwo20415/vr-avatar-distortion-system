using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

public class EgocentricBehaviour : PlayableBehaviour, IKTarget
{
    private Vector3 target;
    
    public override void PrepareFrame(Playable playable, FrameData info) { }
    public override void ProcessFrame(Playable playable, FrameData info, object playerData) 
    {
        EgocentricRayCasterWrapper caster = (EgocentricRayCasterWrapper)playerData;
        List<BSACoordinates> result = caster.GetSourceCoordinates();
        target = caster.SetDestinationCoordinates(result);

        //Debug.Log("Result for Egocentric Self Contact Retargeting, n. of coordinates [" + result.Count + "], weight total, target ended up being " + VExtension.Print(target));
    }

    public Vector3 GetTarget()
    {
        return target;
    }
}

public class EgocentricSelfContact 
{
    //SelfContact Meshes
    private Material m_material;
    private Mesh m_armMesh;
    private float m_armThickness;

    List<GameObject> m_customMeshes;
    List<GameObject> m_cylinders;
    List<GameObject> m_planes;
    private BodySturfaceApproximation m_sourceBSA;
    private BodySturfaceApproximation m_destinationBSA;

    //PlayableGraph
    private List<Playable> m_egoPlayables;
    private List<ScriptPlayableOutput> m_egoOutputs;
    private Dictionary<HumanBodyBones, int> m_hbbConversion;

    public Playable this[int i] { get => m_egoPlayables[i]; }
    public Playable this[HumanBodyBones hbb] { get => m_egoPlayables[m_hbbConversion[hbb]]; }
    public PlayableOutput output(int i) { return m_egoOutputs[i]; }

    public EgocentricSelfContact(Animator src_animator, Animator dest_animator, PlayableGraph graph, Material mat, Mesh arm, float thick, List<CustomAvatarCalibrationMesh> acms, List<HumanBodyBones> joints, EgocentricRayCasterSource.DebugStruct egoDebug)
    {
        m_material = mat;
        m_armMesh = arm;
        m_armThickness = thick;
        
        m_sourceBSA = SetupAvatar(src_animator, acms, joints, egoDebug);
        m_destinationBSA = SetupAvatar(dest_animator, acms, joints, egoDebug);

        { // Add Raycasters & Playables
            m_egoPlayables = new List<Playable>(joints.Count);
            m_egoOutputs = new List<ScriptPlayableOutput>(joints.Count);
            m_hbbConversion = new Dictionary<HumanBodyBones, int>(joints.Count);

            foreach (HumanBodyBones hbb in joints)
            {
                EgocentricRayCasterDestination dest = dest_animator.GetBoneTransform(hbb).gameObject.AddComponent<EgocentricRayCasterDestination>();
                EgocentricRayCasterSource src = src_animator.GetBoneTransform(hbb).gameObject.AddComponent<EgocentricRayCasterSource>();
                EgocentricRayCasterWrapper caster = src_animator.GetBoneTransform(hbb).gameObject.AddComponent<EgocentricRayCasterWrapper>();
                src.Setup(hbb, src_animator, m_sourceBSA, egoDebug);
                dest.Setup(hbb, dest_animator, m_destinationBSA, egoDebug);
                caster.Set(src, dest);
                InstancePlayables(graph, hbb, caster);
            }
        }
    }

    public BodySturfaceApproximation SetupAvatar(Animator animator, List<CustomAvatarCalibrationMesh> acms, List<HumanBodyBones> joints, EgocentricRayCasterSource.DebugStruct egoDebug)
    {
        GameObject parent = new GameObject("Egocentric Stuff");
        parent.transform.parent = animator.avatarRoot;
        parent.transform.localPosition = Vector3.zero;

        m_customMeshes = new List<GameObject>(acms.Count);
        m_cylinders = new List<GameObject>(8);
        m_planes = new List<GameObject>();

        { // Arm Cylinders
            InstanceCylinders(animator, new List<HumanBodyBones>() { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand }, parent.transform, "Left Arm");
            InstanceCylinders(animator, new List<HumanBodyBones>() { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand }, parent.transform, "Right Arm");
            InstanceCylinders(animator, new List<HumanBodyBones>() { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot }, parent.transform, "Left Leg");
            InstanceCylinders(animator, new List<HumanBodyBones>() { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot }, parent.transform, "Right Leg");
        }

        { // Custom Meshes
            foreach (CustomAvatarCalibrationMesh acm in acms)
            {
                InstanceCustomMesh(animator, acm, parent.transform);
                foreach (ExtremitiesPlaneData data in acm.planes)
                {
                    InstanceNormalPlane(animator, data, parent.transform);
                }
            }
        }

        return new BodySturfaceApproximation(animator, m_customMeshes, m_cylinders, m_planes);
    }

    private void InstanceNormalPlane(Animator animator, ExtremitiesPlaneData data, Transform parent)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ObjectBoneFollow follow = obj.AddComponent<ObjectBoneFollow>();
        obj.transform.parent = parent;

        Collider col = obj.GetComponent<Collider>();
        col.enabled = false;

        Transform reference = animator.GetBoneTransform(data.bone);

        follow.calibrate(new List<Transform>() { reference }, reference.position - data.position_offset, Quaternion.Inverse(data.rotation_offset), data.scale);
        m_planes.Add(obj);
    }

    private void InstanceCustomMesh(Animator animator, CustomAvatarCalibrationMesh acm, Transform parent)
    {
        GameObject obj = new GameObject(acm.mesh_name);
        obj.transform.parent = parent;

        MeshFilter filter = obj.AddComponent<MeshFilter>();
        MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
        ObjectBoneFollow follow = obj.AddComponent<ObjectBoneFollow>();

        filter.mesh = acm.getMesh();
        renderer.material = m_material;

        List<Transform> anchors = new List<Transform>();

        foreach (HumanBodyBones hbb in acm.anchors)
        {
            Transform trn = animator.GetBoneTransform(hbb);
            anchors.Add(trn);
            if (!trn) { Debug.Log("No transform found in animator for hbb: " + hbb); }
        }

        Vector3 pos = animator.GetBoneTransform(acm.anchors[0]).position + acm.position_offset;
        follow.calibrate(anchors, pos, acm.rotation_offset, acm.getScale());
        m_customMeshes.Add(obj);
    }
    private void InstanceCylinders(Animator animator, List<HumanBodyBones> bones, Transform parent, string name)
    {
        GameObject group = new GameObject(name);
        group.transform.parent = parent;
        for (int i = 0; i < bones.Count - 1; i++)
        {
            Transform from = animator.GetBoneTransform(bones[i]);
            Transform to = animator.GetBoneTransform(bones[i + 1]);

            GameObject obj = GenerateCylinder(from, to);
            obj.transform.parent = group.transform;
            m_cylinders.Add(obj);
        }
    } 
    private GameObject GenerateCylinder(Transform a, Transform b)
    {

        GameObject capsule = new GameObject("Capsule_" + a.name);
        MeshFilter filter = capsule.AddComponent<MeshFilter>();
        MeshRenderer renderer = capsule.AddComponent<MeshRenderer>();
        ObjectBoneFollow follow = capsule.AddComponent<ObjectBoneFollow>();
        follow.enabled = false;

        filter.mesh = m_armMesh;
        renderer.material = m_material;

        Vector3 position = (a.position + b.position) / 2;
        Vector3 pointer = (a.position - position).normalized;
        float distance = (b.position - a.position).magnitude;
        Quaternion rotation = Quaternion.Inverse(a.rotation) * Quaternion.FromToRotation(Vector3.up, pointer);
        Vector3 scale = new Vector3(m_armThickness, distance / 2, m_armThickness);

        follow.calibrate(new List<Transform>() { a, b }, position, rotation, scale);
        follow.enabled = true;
        return capsule;
    }
    private void InstancePlayables(PlayableGraph graph, HumanBodyBones hbb, EgocentricRayCasterWrapper component)
    {
        ScriptPlayable<EgocentricBehaviour> playable = ScriptPlayable<EgocentricBehaviour>.Create(graph);

        ScriptPlayableOutput output = ScriptPlayableOutput.Create(graph, hbb.ToString() + "_ESC_ScriptOutput");
        output.SetUserData(component);

        //Connections
        //playable.SetOutputCount(2);
        //output.SetSourcePlayable(playable, 0);

        //Store
        m_egoPlayables.Add(playable);
        m_egoOutputs.Add(output);
        m_hbbConversion[hbb] = m_egoPlayables.Count - 1;
    }
}
