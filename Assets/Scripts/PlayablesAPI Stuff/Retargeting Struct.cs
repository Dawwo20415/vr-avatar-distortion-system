using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct VExtension
{
    public static Vector3 FrameChildToParent(Vector3 pPosition, Quaternion pRotation, Vector3 cPosition)
    {
        return Quaternion.Inverse(pRotation) * (cPosition - pPosition);
    }

    public static string Print(Vector3 vec)
    {
        return "[" + vec.x + "," + vec.y + "," + vec.z + "]";
    }
}

public struct QExtension
{
    public static Quaternion ChangeFrame(Quaternion q, Quaternion frame)
    {
        return Quaternion.Inverse(frame) * q * frame;
    }

    public static Quaternion Fix(Quaternion q)
    {
        if (q.eulerAngles == Vector3.zero) { return Quaternion.identity; }
        else { return q; }
    }

    public static Quaternion FromTo(Quaternion from, Quaternion to)
    {
        return Quaternion.Inverse(from) * to;
    }

    public static Quaternion Difference(Quaternion from, Quaternion to)
    {
        return to * Quaternion.Inverse(from);
    }

    public static string Print(Quaternion q)
    {
        return "[" + q.x + "," + q.y + "," + q.z + "," + q.w + "]";
    }

    public static string PrintEuler(Quaternion q)
    {
        return "[" + q.eulerAngles.x + "," + q.eulerAngles.y + "," + q.eulerAngles.z + "]";
    }

    public static Quaternion StackToParent(Transform obj, Transform root, bool include_root)
    {
        Transform destination = include_root ? root.parent : root;
        Quaternion diff = Quaternion.identity;

        do
        {
            diff = obj.localRotation * diff;
            obj = obj.parent;
        } while (obj != destination);

        return diff;
    }
}
