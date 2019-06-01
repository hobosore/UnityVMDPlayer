using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using UnityVMDReader;

public class VMDController : MonoBehaviour
{
    //エラー値の再現に用いる
    readonly Quaternion ZeroQuaternion = new Quaternion(0, 0, 0, 0);

    public bool IsPlaying { get; private set; } = false;
    //IsEndは再生が終了したことを示すフラグで、何の処理にも使用されていない
    public bool IsEnd { get; private set; } = false;
    int frameNumber = 0;
    //モーション終了時に実行させる
    Action endAction = () => { };
    //全ての親はデフォルトでオフ
    public bool UseParentOfAll = false;
    public bool UseCenterIK = true;
    //デフォルトは30fps、垂直同期は切らないと重い?
    //FixedUpdateの値をこれにするので、他と競合があるかもしれない。
    const float FPSs = 0.03333f;
    //ボーン移動量の補正係数
    //この値は経験で決めた大体の値、改良の余地あり
    const float DefaultBoneAmplifier = 0.1f;
    const float ParentAmplifier = 0.1f;
    const float CenterIKAmplifier = 0.1f;
    const float FootIKAmplifier = 0.1f;

    //以下はStart時に初期化
    //animatorはPlay時にも一応初期化
    public Animator Animator { get; private set; }
    //モデルの初期ポーズを保存
    Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, (Transform, float)> boneTransformDictionary;
    Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, Vector3> originalBonePositions;
    Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, Quaternion> originalBoneRotations;

    //以下はPlay時に初期化
    int startedTime;
    Vector3 originalParentLocalPosition;
    Quaternion originalParentLocalRotation;
    FootIK leftFootIK;
    FootIK rightFootIK;
    CenterIK centerIK;
    VMDReader vmdReader;

    //以下はインスペクタにて設定
    public Transform LeftUpperArmTwist;
    public Transform RightUpperArmTwist;
    //VMDファイルのパスを与えて再生するまでオフセットは更新されない
    public Vector3 LeftFootOffset = new Vector3(-0.15f, 0.6f, 0);
    public Vector3 RightFootOffset = new Vector3(0.15f, 0.6f, 0);

    // Start is called before the first frame update
    void Start()
    {
        Time.fixedDeltaTime = FPSs;

        Animator = GetComponent<Animator>();

        boneTransformDictionary = new Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, (Transform, float)>()
        {
            { VMDReader.BoneKeyFrameGroup.BoneNames.下半身 , (Animator.GetBoneTransform(HumanBodyBones.Hips), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左足 , (Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右足 , (Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左ひざ , (Animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右ひざ , (Animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左足首 , (Animator.GetBoneTransform(HumanBodyBones.LeftFoot), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右足首 , (Animator.GetBoneTransform(HumanBodyBones.RightFoot), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.上半身 , (Animator.GetBoneTransform(HumanBodyBones.Spine), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.上半身2 , (Animator.GetBoneTransform(HumanBodyBones.Chest), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.頭 , (Animator.GetBoneTransform(HumanBodyBones.Head), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.首 , (Animator.GetBoneTransform(HumanBodyBones.Neck), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左肩 , (Animator.GetBoneTransform(HumanBodyBones.LeftShoulder), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右肩 , (Animator.GetBoneTransform(HumanBodyBones.RightShoulder), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左腕捩れ , (LeftUpperArmTwist, DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右腕捩れ , (RightUpperArmTwist, DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左腕 , (Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右腕 , (Animator.GetBoneTransform(HumanBodyBones.RightUpperArm), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左ひじ , (Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右ひじ , (Animator.GetBoneTransform(HumanBodyBones.RightLowerArm), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左手首 , (Animator.GetBoneTransform(HumanBodyBones.LeftHand), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右手首 , (Animator.GetBoneTransform(HumanBodyBones.RightHand), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左つま先 , (Animator.GetBoneTransform(HumanBodyBones.LeftToes), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右つま先 , (Animator.GetBoneTransform(HumanBodyBones.RightToes), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左親指１ , (Animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右親指１ , (Animator.GetBoneTransform(HumanBodyBones.RightThumbProximal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左親指２ , (Animator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右親指２ , (Animator.GetBoneTransform(HumanBodyBones.RightThumbIntermediate), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左人指１ , (Animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右人指１ , (Animator.GetBoneTransform(HumanBodyBones.RightIndexProximal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左人指２ , (Animator.GetBoneTransform(HumanBodyBones.LeftIndexIntermediate), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右人指２ , (Animator.GetBoneTransform(HumanBodyBones.RightIndexIntermediate), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左人指３ , (Animator.GetBoneTransform(HumanBodyBones.LeftIndexDistal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右人指３ , (Animator.GetBoneTransform(HumanBodyBones.RightIndexDistal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左中指１ , (Animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右中指１ , (Animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左中指２ , (Animator.GetBoneTransform(HumanBodyBones.LeftMiddleIntermediate), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右中指２ , (Animator.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左中指３ , (Animator.GetBoneTransform(HumanBodyBones.LeftMiddleDistal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右中指３ , (Animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左薬指１ , (Animator.GetBoneTransform(HumanBodyBones.LeftRingProximal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右薬指１ , (Animator.GetBoneTransform(HumanBodyBones.RightRingProximal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左薬指２ , (Animator.GetBoneTransform(HumanBodyBones.LeftRingIntermediate), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右薬指２ , (Animator.GetBoneTransform(HumanBodyBones.RightRingIntermediate), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左薬指３ , (Animator.GetBoneTransform(HumanBodyBones.LeftRingDistal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右薬指３ , (Animator.GetBoneTransform(HumanBodyBones.RightRingDistal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左小指１ , (Animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右小指１ , (Animator.GetBoneTransform(HumanBodyBones.RightLittleProximal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左小指２ , (Animator.GetBoneTransform(HumanBodyBones.LeftLittleIntermediate), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右小指２ , (Animator.GetBoneTransform(HumanBodyBones.RightLittleIntermediate), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左小指３ , (Animator.GetBoneTransform(HumanBodyBones.LeftLittleDistal), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右小指３ , (Animator.GetBoneTransform(HumanBodyBones.RightLittleDistal), DefaultBoneAmplifier) }
        };

        //モデルの初期ポーズを保存
        originalBonePositions = new Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, Vector3>();
        originalBoneRotations = new Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, Quaternion>();
        int count = VMDReader.BoneKeyFrameGroup.BoneStringNames.Count;
        for (int i = 0; i < count; i++)
        {
            VMDReader.BoneKeyFrameGroup.BoneNames boneName = (VMDReader.BoneKeyFrameGroup.BoneNames)VMDReader.BoneKeyFrameGroup.BoneStringNames.IndexOf(VMDReader.BoneKeyFrameGroup.BoneStringNames[i]);
            if (!boneTransformDictionary.Keys.Contains(boneName) || boneTransformDictionary[boneName].Item1 == null) { continue; }
            originalBonePositions.Add(boneName, boneTransformDictionary[boneName].Item1.localPosition);
            originalBoneRotations.Add(boneName, boneTransformDictionary[boneName].Item1.localRotation);
        }
    }

    private void FixedUpdate()
    {
        if (!IsPlaying) { return; }

        //最終フレームを超えれば終了
        if (vmdReader.FrameCount < frameNumber)
        {
            IsPlaying = false;
            IsEnd = true;
            //最後にすることがあれば
            endAction();
            return;
        }

        frameNumber++;

        //現在のフレーム数に有効なフレームがあればボーンを動かす
        Animate(frameNumber);

        //補間
        Interpolate(frameNumber);
    }

    // Update is called once per frame
    void Update()
    {

    }

    void OnDrawGizmosSelected()
    {
        Animator = GetComponent<Animator>();
        Transform leftFoot = Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot = Animator.GetBoneTransform(HumanBodyBones.RightFoot);
        Gizmos.DrawWireSphere(leftFoot.position + leftFoot.rotation * LeftFootOffset, 0.1f);
        Gizmos.DrawWireSphere(rightFoot.position + rightFoot.rotation * RightFootOffset, 0.1f);
    }

    public void SetFPS(int fps)
    {
        Time.fixedDeltaTime = 1 / (float)fps;
    }

    public void Stop()
    {
        IsPlaying = false;
        Animator = GetComponent<Animator>();
        Animator.enabled = true;
    }

    public void Play()
    {
        IsPlaying = true;
    }

    public void Play(Action endAction)
    {
        this.endAction = endAction;
        Play();
    }

    //こいつがPlayの本体みたいなもの
    public void Play(string filePath)
    {
        frameNumber = 0;
        vmdReader = new VMDReader(filePath);

        Animator = GetComponent<Animator>();
        Animator.enabled = false;

        originalParentLocalPosition = transform.localPosition;
        originalParentLocalRotation = transform.localRotation;

        //モデルに初期ポーズを取らせる
        foreach (VMDReader.BoneKeyFrameGroup.BoneNames boneName in originalBonePositions.Keys)
        {
            boneTransformDictionary[boneName].Item1.localPosition = originalBonePositions[boneName];
            boneTransformDictionary[boneName].Item1.localRotation = Quaternion.identity;
        }

        //frame = 0において初期化
        centerIK = null;
        leftFootIK = null;
        rightFootIK = null;
        centerIK = new CenterIK(vmdReader, Animator);
        Animate(frameNumber);
        leftFootIK = new FootIK(vmdReader, Animator, FootIK.Feet.LeftFoot, LeftFootOffset);
        rightFootIK = new FootIK(vmdReader, Animator, FootIK.Feet.RightFoot, RightFootOffset);
        Animate(frameNumber);

        Play();
    }

    public void Play(string filePath, Action endAction)
    {
        this.endAction = endAction;
        Play(filePath);
    }

    public void Play(string filePath, bool useCenterIK, bool useParentOfAll)
    {
        UseCenterIK = useCenterIK;
        useParentOfAll = UseParentOfAll;
        Play(filePath);
    }

    public void Play(string filePath, bool useCenterIK, bool useParentOfAll, Action endAction)
    {
        this.endAction = endAction;
        Play(filePath, useCenterIK, useParentOfAll);
    }

    //あまりテストしていない
    public void JumpToFrame(int frameNumber)
    {
        this.frameNumber = frameNumber;
        Animate(frameNumber);
        Interpolate(frameNumber);
    }

    public void Pause()
    {
        IsPlaying = false;
    }

    void Animate(int frameNumber)
    {
        void animateParentOfAll(float amp = ParentAmplifier)
        {
            VMD.BoneKeyFrame parentBoneFrame = vmdReader.GetBoneKeyFrame(VMDReader.BoneKeyFrameGroup.BoneNames.全ての親, frameNumber);
            if (parentBoneFrame == null) { parentBoneFrame = new VMD.BoneKeyFrame(); }
            if (parentBoneFrame.Position != Vector3.zero)
            {
                transform.localPosition = originalParentLocalPosition + parentBoneFrame.Position * amp;
            }
            if (parentBoneFrame.Rotation != ZeroQuaternion)
            {
                transform.localRotation = originalParentLocalRotation.PlusRotation(parentBoneFrame.Rotation);
            }
        }

        void animateBone(VMDReader.BoneKeyFrameGroup.BoneNames boneName)
        {
            Transform boneTransform = boneTransformDictionary[boneName].Item1;
            if (boneTransform == null) { return; }
            VMD.BoneKeyFrame vmdBoneFrame = vmdReader.GetBoneKeyFrame(boneName, frameNumber);
            if (vmdBoneFrame == null) { return; }

            if (vmdBoneFrame.Position != Vector3.zero)
            {
                boneTransform.localPosition = originalBonePositions[boneName] + vmdBoneFrame.Position * boneTransformDictionary[boneName].Item2;
            }
            if (vmdBoneFrame.Rotation != ZeroQuaternion)
            {
                boneTransform.localRotation = originalBoneRotations[boneName].PlusRotation(vmdBoneFrame.Rotation);
            }
        }

        if (UseParentOfAll) { animateParentOfAll(); }
        foreach (VMDReader.BoneKeyFrameGroup.BoneNames boneName in boneTransformDictionary.Keys) { animateBone(boneName); }
        if (UseCenterIK && centerIK != null) { centerIK.IK(frameNumber); }
        if (leftFootIK != null) { leftFootIK.IK(frameNumber); }
        if (rightFootIK != null) { rightFootIK.IK(frameNumber); }
    }

    void Interpolate(int frameNumber)
    {
        void interpolateParentOfAll(float amp = ParentAmplifier)
        {
            VMDReader.BoneKeyFrameGroup vmdBoneFrameGroup = vmdReader.GetBoneKeyFrameGroup(VMDReader.BoneKeyFrameGroup.BoneNames.全ての親);
            VMD.BoneKeyFrame lastPositionVMDBoneFrame = vmdBoneFrameGroup.LastPositionKeyFrame;
            VMD.BoneKeyFrame lastRotationVMDBoneFrame = vmdBoneFrameGroup.LastRotationKeyFrame;
            VMD.BoneKeyFrame nextPositionVMDBoneFrame = vmdBoneFrameGroup.NextPositionKeyFrame;
            VMD.BoneKeyFrame nextRotationVMDBoneFrame = vmdBoneFrameGroup.NextRotationKeyFrame;

            if (nextPositionVMDBoneFrame != null && lastPositionVMDBoneFrame != null)
            {
                float xInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, vmdBoneFrameGroup.LastPositionKeyFrame.Frame, vmdBoneFrameGroup.NextPositionKeyFrame.Frame);
                float yInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, vmdBoneFrameGroup.LastPositionKeyFrame.Frame, vmdBoneFrameGroup.NextPositionKeyFrame.Frame);
                float zInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, vmdBoneFrameGroup.LastPositionKeyFrame.Frame, vmdBoneFrameGroup.NextPositionKeyFrame.Frame);

                float xInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.x, nextPositionVMDBoneFrame.Position.x, xInterpolationRate);
                float yInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.y, nextPositionVMDBoneFrame.Position.y, yInterpolationRate);
                float zInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.z, nextPositionVMDBoneFrame.Position.z, zInterpolationRate);
                transform.localPosition = originalParentLocalPosition + new Vector3(xInterpolation, yInterpolation, zInterpolation) * DefaultBoneAmplifier;
            }

            if (nextRotationVMDBoneFrame != null && lastRotationVMDBoneFrame != null)
            {
                float rotationInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, vmdBoneFrameGroup.LastRotationKeyFrame.Frame, vmdBoneFrameGroup.NextRotationKeyFrame.Frame);
                transform.localRotation = originalParentLocalRotation.PlusRotation(Quaternion.Lerp(lastRotationVMDBoneFrame.Rotation, nextRotationVMDBoneFrame.Rotation, rotationInterpolationRate));
            }
        }

        void interpolateBone(VMDReader.BoneKeyFrameGroup.BoneNames boneName)
        {
            Transform boneTransform = boneTransformDictionary[boneName].Item1;

            VMDReader.BoneKeyFrameGroup vmdBoneFrameGroup = vmdReader.GetBoneKeyFrameGroup(boneName);
            VMD.BoneKeyFrame lastPositionVMDBoneFrame = vmdBoneFrameGroup.LastPositionKeyFrame;
            VMD.BoneKeyFrame lastRotationVMDBoneFrame = vmdBoneFrameGroup.LastRotationKeyFrame;
            VMD.BoneKeyFrame nextPositionVMDBoneFrame = vmdBoneFrameGroup.NextPositionKeyFrame;
            VMD.BoneKeyFrame nextRotationVMDBoneFrame = vmdBoneFrameGroup.NextRotationKeyFrame;

            if (boneTransform == null) { return; }

            if (nextPositionVMDBoneFrame != null && lastPositionVMDBoneFrame != null)
            {
                float xInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, lastPositionVMDBoneFrame.Frame, nextPositionVMDBoneFrame.Frame);
                float yInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, lastPositionVMDBoneFrame.Frame, nextPositionVMDBoneFrame.Frame);
                float zInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, lastPositionVMDBoneFrame.Frame, nextPositionVMDBoneFrame.Frame);

                float xInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.x, nextPositionVMDBoneFrame.Position.x, xInterpolationRate);
                float yInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.y, nextPositionVMDBoneFrame.Position.y, yInterpolationRate);
                float zInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.z, nextPositionVMDBoneFrame.Position.z, zInterpolationRate);
                boneTransform.localPosition = originalBonePositions[boneName] + new Vector3(xInterpolation, yInterpolation, zInterpolation) * DefaultBoneAmplifier;
            }

            if (nextRotationVMDBoneFrame != null && lastRotationVMDBoneFrame != null)
            {
                float rotationInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, lastRotationVMDBoneFrame.Frame, nextRotationVMDBoneFrame.Frame);
                boneTransform.localRotation = originalBoneRotations[boneName].PlusRotation(Quaternion.Lerp(lastRotationVMDBoneFrame.Rotation, nextRotationVMDBoneFrame.Rotation, rotationInterpolationRate));
            }
        }

        if (UseParentOfAll) { interpolateParentOfAll(); }
        foreach (VMDReader.BoneKeyFrameGroup.BoneNames boneName in boneTransformDictionary.Keys) { interpolateBone(boneName); }
        if (UseCenterIK) { centerIK.InterpolateIK(frameNumber); }
        leftFootIK.InterpolateIK(frameNumber);
        rightFootIK.InterpolateIK(frameNumber);
    }

    //VMDではセンターはHipの差分のみの位置、回転情報を持つ
    //グルーブのみを用いてセンターを用いないモーションは不具合を起こす
    class CenterIK
    {
        readonly Quaternion ZeroQuaternion = new Quaternion(0, 0, 0, 0);

        VMDReader.BoneKeyFrameGroup.BoneNames centerBoneName = VMDReader.BoneKeyFrameGroup.BoneNames.センター;
        VMDReader.BoneKeyFrameGroup.BoneNames grooveBoneName = VMDReader.BoneKeyFrameGroup.BoneNames.グルーブ;

        public VMDReader VMDReader { get; private set; }
        //デフォルトでtrueであることに注意
        public bool Enable { get; private set; } = true;
        public Animator Animator { get; private set; }
        Transform target { get; set; }
        Vector3 firstLocalPostion { get; set; }

        public CenterIK(VMDReader vmdReader, Animator animator)
        {
            VMDReader = vmdReader;
            Animator = animator;
            target = Animator.GetBoneTransform(HumanBodyBones.Hips);
            firstLocalPostion = target.localPosition;
        }

        public void IK(int frame)
        {
            //InterpolateIK(frame)でSetIKEnableを呼び出すため、ここではSetIKEnableを呼び出さない

            VMD.BoneKeyFrame centerBoneFrame = VMDReader.GetBoneKeyFrame(centerBoneName, frame);
            VMD.BoneKeyFrame grooveBoneFrame = VMDReader.GetBoneKeyFrame(grooveBoneName, frame);

            InterpolateIK(frame);
        }

        public void InterpolateIK(int frame)
        {
            SetIKEnable(frame);

            if (!Enable) { return; }


            VMDReader.BoneKeyFrameGroup centerVMDBoneFrameGroup = VMDReader.GetBoneKeyFrameGroup(centerBoneName);
            VMDReader.BoneKeyFrameGroup grooveVMDBoneFrameGroup = VMDReader.GetBoneKeyFrameGroup(grooveBoneName);

            VMD.BoneKeyFrame centerLastPositionVMDBoneFrame = centerVMDBoneFrameGroup.LastPositionKeyFrame;
            VMD.BoneKeyFrame centerLastRotationVMDBoneFrame = centerVMDBoneFrameGroup.LastRotationKeyFrame;
            VMD.BoneKeyFrame centerNextPositionVMDBoneFrame = centerVMDBoneFrameGroup.NextPositionKeyFrame;
            VMD.BoneKeyFrame centerNextRotationVMDBoneFrame = centerVMDBoneFrameGroup.NextRotationKeyFrame;

            VMD.BoneKeyFrame grooveLastPositionVMDBoneFrame = grooveVMDBoneFrameGroup.LastPositionKeyFrame;
            VMD.BoneKeyFrame grooveLastRotationVMDBoneFrame = grooveVMDBoneFrameGroup.LastRotationKeyFrame;
            VMD.BoneKeyFrame grooveNextPositionVMDBoneFrame = grooveVMDBoneFrameGroup.NextPositionKeyFrame;
            VMD.BoneKeyFrame grooveNextRotationVMDBoneFrame = grooveVMDBoneFrameGroup.NextRotationKeyFrame;

            if (centerNextPositionVMDBoneFrame != null && centerLastPositionVMDBoneFrame != null)
            {
                float xCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frame, centerVMDBoneFrameGroup.LastPositionKeyFrame.Frame, centerVMDBoneFrameGroup.NextPositionKeyFrame.Frame);
                float yCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frame, centerVMDBoneFrameGroup.LastPositionKeyFrame.Frame, centerVMDBoneFrameGroup.NextPositionKeyFrame.Frame);
                float zCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frame, centerVMDBoneFrameGroup.LastPositionKeyFrame.Frame, centerVMDBoneFrameGroup.NextPositionKeyFrame.Frame);

                float xCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.x, centerNextPositionVMDBoneFrame.Position.x, xCenterInterpolationRate);
                float yCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.y, centerNextPositionVMDBoneFrame.Position.y, yCenterInterpolationRate);
                float zCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.z, centerNextPositionVMDBoneFrame.Position.z, zCenterInterpolationRate);
                target.localPosition = firstLocalPostion + new Vector3(xCenterInterpolation, yCenterInterpolation, zCenterInterpolation) * CenterIKAmplifier;

                if (grooveNextPositionVMDBoneFrame != null && grooveLastPositionVMDBoneFrame != null)
                {
                    float xGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frame, grooveVMDBoneFrameGroup.LastPositionKeyFrame.Frame, grooveVMDBoneFrameGroup.NextPositionKeyFrame.Frame);
                    float yGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frame, grooveVMDBoneFrameGroup.LastPositionKeyFrame.Frame, grooveVMDBoneFrameGroup.NextPositionKeyFrame.Frame);
                    float zGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frame, grooveVMDBoneFrameGroup.LastPositionKeyFrame.Frame, grooveVMDBoneFrameGroup.NextPositionKeyFrame.Frame);

                    float xGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.x, grooveNextPositionVMDBoneFrame.Position.x, xGrooveInterpolationRate);
                    float yGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.y, grooveNextPositionVMDBoneFrame.Position.y, yGrooveInterpolationRate);
                    float zGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.z, grooveNextPositionVMDBoneFrame.Position.z, zGrooveInterpolationRate);
                    target.localPosition += new Vector3(xGrooveInterpolation, yGrooveInterpolation, zGrooveInterpolation) * CenterIKAmplifier;
                }
            }

            if (centerNextRotationVMDBoneFrame != null && centerLastRotationVMDBoneFrame != null)
            {
                float centerRotationInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frame, centerVMDBoneFrameGroup.LastRotationKeyFrame.Frame, centerVMDBoneFrameGroup.NextRotationKeyFrame.Frame);
                target.localRotation = Quaternion.Lerp(centerLastRotationVMDBoneFrame.Rotation, centerNextRotationVMDBoneFrame.Rotation, centerRotationInterpolationRate);

                if (grooveNextPositionVMDBoneFrame != null && grooveLastPositionVMDBoneFrame != null)
                {
                    float grooveRotationInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frame, grooveVMDBoneFrameGroup.LastRotationKeyFrame.Frame, grooveVMDBoneFrameGroup.NextRotationKeyFrame.Frame);
                    target.localRotation = target.localRotation.PlusRotation(Quaternion.Lerp(grooveLastRotationVMDBoneFrame.Rotation, grooveNextRotationVMDBoneFrame.Rotation, grooveRotationInterpolationRate));
                }
            }
        }

        //内部でIKのEnableの値を設定しているが、VMDのIKの設定にセンターの有効無効は含まれない？
        private void SetIKEnable(int frame)
        {
            VMD.IKKeyFrame currentIKFrame = VMDReader.RawVMD.IKFrames.Find(x => x.Frame == frame);
            if (currentIKFrame != null)
            {
                VMD.IKKeyFrame.VMDIKEnable currentIKEnable = currentIKFrame.IKEnable.Find((VMD.IKKeyFrame.VMDIKEnable x) => x.IKName == centerBoneName.ToString());
                if (currentIKEnable != null)
                {
                    Enable = currentIKEnable.Enable;
                }
            }
        }
    }

    //VMDでは足IKはFootの差分のみの位置、回転情報を持つ
    //また、このコードで足先IKは未実装である
    class FootIK
    {
        public enum Feet { LeftFoot, RightFoot }

        public VMDReader VMDReader { get; private set; }
        //デフォルトでtrueであることに注意
        public bool Enable { get; private set; } = true;
        public Feet Foot { get; private set; }
        public Animator Animator { get; private set; }
        public Vector3 Offset { get; private set; }
        public Transform HipTransform { get; private set; }
        public Transform KneeTransform { get; private set; }
        public Transform FootTransform { get; private set; }
        public Transform Target { get; private set; }
        private VMDReader.BoneKeyFrameGroup.BoneNames boneName { get; set; }
        private Vector3 firstLocalPosition { get; set; }

        private float upperLegLength = 0;
        private float lowerLegLength = 0;
        private float legLength = 0;
        private float targetDistance = 0;

        private void Initialize(VMDReader vmdReader, Animator animator, Feet foot, Vector3 offset)
        {
            VMDReader = vmdReader;
            Foot = foot;
            Animator = animator;
            //注意！オフセットのy座標を逆にしている
            Offset = new Vector3(offset.x, -offset.y, offset.z);

            if (Foot == Feet.LeftFoot)
            {
                boneName = VMDReader.BoneKeyFrameGroup.BoneNames.左足ＩＫ;
                HipTransform = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                KneeTransform = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                FootTransform = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            }
            else
            {
                boneName = VMDReader.BoneKeyFrameGroup.BoneNames.右足ＩＫ;
                HipTransform = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                KneeTransform = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                FootTransform = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
            upperLegLength = Vector3.Distance(HipTransform.position, KneeTransform.position);
            lowerLegLength = Vector3.Distance(KneeTransform.position, FootTransform.position);
            legLength = upperLegLength + lowerLegLength;

            firstLocalPosition = FootTransform.localPosition;
        }
        public FootIK(VMDReader vmdReader, Animator animator, Feet foot, Transform target, Vector3 offset)
        {
            Initialize(vmdReader, animator, foot, offset);
            Target = target;
        }
        public FootIK(VMDReader vmdReader, Animator animator, Feet foot, Vector3 offset)
        {
            Initialize(vmdReader, animator, foot, offset);
            GameObject targetGameObject = new GameObject();
            targetGameObject.transform.position = FootTransform.position;
            targetGameObject.transform.parent = Animator.transform;
            Target = targetGameObject.transform;
        }

        public void IK()
        {
            Vector3 targetVector = Target.position - HipTransform.position;

            targetDistance = Mathf.Min(targetVector.magnitude, legLength);

            float hipAdjacent = ((upperLegLength * upperLegLength) - (lowerLegLength * lowerLegLength) + (targetDistance * targetDistance)) / (2 * targetDistance);
            float hipAngle;
            float hipAngleCos = hipAdjacent / upperLegLength;
            //1や1fではエラー
            if (hipAngleCos > 0.999f) { hipAngle = 0; }
            else if (hipAngleCos < -0.999f) { hipAngle = Mathf.PI * Mathf.Rad2Deg; }
            else { hipAngle = Mathf.Acos(hipAngleCos) * Mathf.Rad2Deg; }

            float kneeAdjacent = ((upperLegLength * upperLegLength) + (lowerLegLength * lowerLegLength) - (targetDistance * targetDistance)) / (2 * lowerLegLength);
            float kneeAngle;
            float kneeAngleCos = kneeAdjacent / upperLegLength;
            //1や1fではエラー
            if (kneeAngleCos > 0.999f) { kneeAngle = 0; }
            else if (kneeAngleCos < -0.999f)
            {
                kneeAngle = Mathf.PI * Mathf.Rad2Deg;

                //三角形がつぶれすぎると成立条件が怪しくなりひざの角度が180度になるなど挙動が乱れる
                if (hipAngle == 0) { kneeAngle = 0; }
            }
            else { kneeAngle = 180 - Mathf.Acos(kneeAdjacent / upperLegLength) * Mathf.Rad2Deg; }

            HipTransform.localRotation = Quaternion.identity;
            HipTransform.RotateAround(HipTransform.position, Vector3.Cross(-HipTransform.up, targetVector), Vector3.Angle(-HipTransform.up, targetVector));
            HipTransform.RotateAround(HipTransform.position, HipTransform.right, -hipAngle);
            KneeTransform.localRotation = Quaternion.identity;
            KneeTransform.RotateAround(KneeTransform.position, HipTransform.right, kneeAngle);
        }
        public void IK(int frame)
        {
            SetIKEnable(frame);

            if (!Enable) { return; }

            VMD.BoneKeyFrame footIKFrame = VMDReader.GetBoneKeyFrame(boneName, frame);

            if (footIKFrame == null || footIKFrame.Position == Vector3.zero) { return; }

            Target.localPosition = firstLocalPosition + footIKFrame.Position * FootIKAmplifier + FootTransform.localRotation * Offset;

            IK();
        }

        public void InterpolateIK(int frame)
        {
            SetIKEnable(frame);

            if (!Enable) { return; }

            VMDReader.BoneKeyFrameGroup vmdBoneFrameGroup = VMDReader.GetBoneKeyFrameGroup(boneName);
            VMD.BoneKeyFrame lastPositionVMDBoneFrame = vmdBoneFrameGroup.LastPositionKeyFrame;
            VMD.BoneKeyFrame lastRotationVMDBoneFrame = vmdBoneFrameGroup.LastRotationKeyFrame;
            VMD.BoneKeyFrame nextPositionVMDBoneFrame = vmdBoneFrameGroup.NextPositionKeyFrame;
            VMD.BoneKeyFrame nextRotationVMDBoneFrame = vmdBoneFrameGroup.NextRotationKeyFrame;

            if (nextPositionVMDBoneFrame != null && lastPositionVMDBoneFrame != null)
            {
                float xInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frame, lastPositionVMDBoneFrame.Frame, nextPositionVMDBoneFrame.Frame);
                float yInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frame, lastPositionVMDBoneFrame.Frame, nextPositionVMDBoneFrame.Frame);
                float zInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frame, lastPositionVMDBoneFrame.Frame, nextPositionVMDBoneFrame.Frame);

                float xInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.x, nextPositionVMDBoneFrame.Position.x, xInterpolationRate);
                float yInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.y, nextPositionVMDBoneFrame.Position.y, yInterpolationRate);
                float zInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.z, nextPositionVMDBoneFrame.Position.z, zInterpolationRate);

                Target.localPosition = firstLocalPosition + new Vector3(xInterpolation, yInterpolation, zInterpolation) * FootIKAmplifier + FootTransform.localRotation * Offset;
            }

            if (nextRotationVMDBoneFrame != null && lastRotationVMDBoneFrame != null)
            {
                float rotationInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frame, vmdBoneFrameGroup.LastRotationKeyFrame.Frame, vmdBoneFrameGroup.NextRotationKeyFrame.Frame);
                Target.localRotation = Quaternion.Lerp(lastRotationVMDBoneFrame.Rotation, nextRotationVMDBoneFrame.Rotation, rotationInterpolationRate);
            }

            IK();
        }

        //内部でIKのEnableの値を設定している
        private void SetIKEnable(int frame)
        {
            VMD.IKKeyFrame currentIKFrame = VMDReader.RawVMD.IKFrames.Find(x => x.Frame == frame);
            if (currentIKFrame != null)
            {
                VMD.IKKeyFrame.VMDIKEnable currentIKEnable = currentIKFrame.IKEnable.Find((VMD.IKKeyFrame.VMDIKEnable x) => x.IKName == boneName.ToString());
                if (currentIKEnable != null)
                {
                    Enable = currentIKEnable.Enable;
                }
            }
        }
    }
}
