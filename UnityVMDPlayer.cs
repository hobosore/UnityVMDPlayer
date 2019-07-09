using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using UnityVMDReader;

public class UnityVMDPlayer : MonoBehaviour
{
    public bool IsPlaying { get; private set; } = false;
    //IsEndは再生が終了したことを示すフラグで、何の処理にも使用されていない
    public bool IsEnd { get; private set; } = false;
    int frameNumber = 0;
    //モーション終了時に実行させる
    Action endAction = () => { };
    public bool IsLoop = false;
    //全ての親はデフォルトでオン
    public bool UseParentOfAll = true;
    //下半身の回転はまだ開発中
    public bool UseLegRotationBeta = false;
    //デフォルトは30fps、垂直同期は切らないと重いことがある?
    //FixedUpdateの値をこれにするので、他と競合があるかもしれない。
    const float FPSs = 0.03333f;
    //ボーン移動量の補正係数
    //この値は大体の値、改良の余地あり
    const float DefaultBoneAmplifier = 0.06f;
    const float ParentAmplifier = 0.06f;
    const float CenterIKAmplifier = 0.06f;
    const float FootIKAmplifier = 0.06f;
    //エラー値の再現に用いる
    readonly Quaternion ZeroQuaternion = new Quaternion(0, 0, 0, 0);


    //以下はStart時に初期化
    //animatorはPlay時にも一応初期化
    public Animator Animator { get; private set; }
    //モデルの初期ポーズを保存
    Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, (Transform, float)> upperBoneTransformDictionary;
    Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, Vector3> upperBoneOriginalPositions;
    Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, Quaternion> upperBoneOriginalRotations;
    Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, (Transform, float)> leftLowerBoneTransformDictionary;
    Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, Quaternion> leftLowerBoneOriginalRotations;
    Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, (Transform, float)> rightLowerBoneTransformDictionary;
    Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, Quaternion> rightLowerBoneOriginalRotations;

    //以下はPlay時に初期化
    int startedTime;
    Vector3 originalParentLocalPosition;
    Quaternion originalParentLocalRotation;
    FootIK leftFootIK;
    FootIK rightFootIK;
    CenterAnimation centerIK;
    VMDReader vmdReader;

    //以下はインスペクタにて設定
    public Transform LeftUpperArmTwist;
    public Transform RightUpperArmTwist;
    //VMDファイルのパスを与えて再生するまでオフセットは更新されない
    public Vector3 LeftFootOffset = new Vector3(-0.15f, 0, 0);
    public Vector3 RightFootOffset = new Vector3(0.15f, 0, 0);

    // Start is called before the first frame update
    void Start()
    {
        Time.fixedDeltaTime = FPSs;

        Animator = GetComponent<Animator>();

        upperBoneTransformDictionary = new Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, (Transform, float)>()
        {
            //センターはHips
            //下半身などというものはUnityにはないので、センターとともに処理
            { VMDReader.BoneKeyFrameGroup.BoneNames.上半身 ,   (Animator.GetBoneTransform(HumanBodyBones.Spine), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.上半身2 ,  (Animator.GetBoneTransform(HumanBodyBones.Chest), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.頭 ,       (Animator.GetBoneTransform(HumanBodyBones.Head), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.首 ,       (Animator.GetBoneTransform(HumanBodyBones.Neck), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左肩 ,     (Animator.GetBoneTransform(HumanBodyBones.LeftShoulder), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右肩 ,     (Animator.GetBoneTransform(HumanBodyBones.RightShoulder), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左腕捩れ , (LeftUpperArmTwist, DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右腕捩れ , (RightUpperArmTwist, DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左腕 ,     (Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右腕 ,     (Animator.GetBoneTransform(HumanBodyBones.RightUpperArm), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左ひじ ,   (Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右ひじ ,   (Animator.GetBoneTransform(HumanBodyBones.RightLowerArm), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.左手首 ,   (Animator.GetBoneTransform(HumanBodyBones.LeftHand), DefaultBoneAmplifier) },
            { VMDReader.BoneKeyFrameGroup.BoneNames.右手首 ,   (Animator.GetBoneTransform(HumanBodyBones.RightHand), DefaultBoneAmplifier) },
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
        leftLowerBoneTransformDictionary = new Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, (Transform, float)>()
        {
            //下半身などというものはUnityにはないので、センターとともに処理
            { VMDReader.BoneKeyFrameGroup.BoneNames.左足 ,     (Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), DefaultBoneAmplifier) },
            //ひざボーンはほとんど動けないので実際のところ無視してよい
            //{ VMDReader.BoneKeyFrameGroup.BoneNames.左ひざ ,   (Animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), DefaultBoneAmplifier) },
            //足首ボーンはほとんど動けないので実際のところ無視してよい
            //{ VMDReader.BoneKeyFrameGroup.BoneNames.左足首 ,   (Animator.GetBoneTransform(HumanBodyBones.LeftFoot), DefaultBoneAmplifier) },
        };
        rightLowerBoneTransformDictionary = new Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, (Transform, float)>()
        {
            //下半身などというものはUnityにはないので、センターとともに処理
            { VMDReader.BoneKeyFrameGroup.BoneNames.右足 ,     (Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg), DefaultBoneAmplifier) },
            //ひざボーンはほとんど動けないので実際のところ無視してよい
            //{ VMDReader.BoneKeyFrameGroup.BoneNames.右ひざ ,   (Animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), DefaultBoneAmplifier) },
            //足首ボーンはほとんど動けないので実際のところ無視してよい
            //{ VMDReader.BoneKeyFrameGroup.BoneNames.右足首 ,   (Animator.GetBoneTransform(HumanBodyBones.RightFoot), DefaultBoneAmplifier) },
        };

        //モデルの初期ポーズを保存
        upperBoneOriginalPositions = new Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, Vector3>();
        upperBoneOriginalRotations = new Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, Quaternion>();
        int count = VMDReader.BoneKeyFrameGroup.BoneStringNames.Count;
        for (int i = 0; i < count; i++)
        {
            VMDReader.BoneKeyFrameGroup.BoneNames boneName = (VMDReader.BoneKeyFrameGroup.BoneNames)VMDReader.BoneKeyFrameGroup.BoneStringNames.IndexOf(VMDReader.BoneKeyFrameGroup.BoneStringNames[i]);
            if (!upperBoneTransformDictionary.Keys.Contains(boneName) || upperBoneTransformDictionary[boneName].Item1 == null) { continue; }
            upperBoneOriginalPositions.Add(boneName, upperBoneTransformDictionary[boneName].Item1.localPosition);
            upperBoneOriginalRotations.Add(boneName, upperBoneTransformDictionary[boneName].Item1.localRotation);
        }

        leftLowerBoneOriginalRotations = new Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, Quaternion>();
        for (int i = 0; i < count; i++)
        {
            VMDReader.BoneKeyFrameGroup.BoneNames boneName = (VMDReader.BoneKeyFrameGroup.BoneNames)VMDReader.BoneKeyFrameGroup.BoneStringNames.IndexOf(VMDReader.BoneKeyFrameGroup.BoneStringNames[i]);
            if (!leftLowerBoneTransformDictionary.Keys.Contains(boneName) || leftLowerBoneTransformDictionary[boneName].Item1 == null) { continue; }
            leftLowerBoneOriginalRotations.Add(boneName, leftLowerBoneTransformDictionary[boneName].Item1.localRotation);
        }

        rightLowerBoneOriginalRotations = new Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, Quaternion>();
        for (int i = 0; i < count; i++)
        {
            VMDReader.BoneKeyFrameGroup.BoneNames boneName = (VMDReader.BoneKeyFrameGroup.BoneNames)VMDReader.BoneKeyFrameGroup.BoneStringNames.IndexOf(VMDReader.BoneKeyFrameGroup.BoneStringNames[i]);
            if (!rightLowerBoneTransformDictionary.Keys.Contains(boneName) || rightLowerBoneTransformDictionary[boneName].Item1 == null) { continue; }
            rightLowerBoneOriginalRotations.Add(boneName, rightLowerBoneTransformDictionary[boneName].Item1.localRotation);
        }

    }

    private void FixedUpdate()
    {
        if (!IsPlaying) { return; }

        //最終フレームを超えれば終了
        if (vmdReader.FrameCount <= frameNumber)
        {
            if (IsLoop)
            {
                JumpToFrame(0);
                return;
            }

            IsPlaying = false;
            IsEnd = true;
            //最後にすることがあれば
            endAction();
            return;
        }

        frameNumber++;

        //全ての親を動かす
        if (UseParentOfAll) { AnimateParentOfAll(); }
        //全ての親の補間
        if (UseParentOfAll) { InterpolateParentOfAll(); }

        //腰から上を動かす
        AnimateUpperBody(frameNumber);
        //腰から上の補間
        InterpolateUpperBody(frameNumber);

        //センター
        if (centerIK != null) { centerIK.Animate(frameNumber); }

        //センターの補間
        if (centerIK != null) { centerIK.Interpolate(frameNumber); }

        //下半身の補完
        if (centerIK != null) { centerIK.Complement(frameNumber); }

        //足IKを動かす
        //当該フレームで足IKの実値があったかどうか
        bool leftFootIKExists = false;
        bool rightFootIKExists = false;
        if (leftFootIK != null) { leftFootIKExists = leftFootIK.IK(frameNumber); }
        if (rightFootIK != null) { rightFootIKExists = rightFootIK.IK(frameNumber); }

        //腰から下を動かす
        if (UseLegRotationBeta)
        {
            if (leftFootIKExists) { AnimateLowerBody(frameNumber, leftLowerBoneTransformDictionary); }
            if (rightFootIKExists) { AnimateLowerBody(frameNumber, rightLowerBoneTransformDictionary); }
        }

        //足IKの補間
        if (leftFootIK != null) { leftFootIK.InterpolateIK(frameNumber); }
        if (rightFootIK != null) { rightFootIK.InterpolateIK(frameNumber); }

        //腰から下の補間
        if (UseLegRotationBeta)
        {
            InterpolateLowerBody(frameNumber, leftLowerBoneTransformDictionary);
            InterpolateLowerBody(frameNumber, rightLowerBoneTransformDictionary);
        }
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
        Gizmos.DrawWireSphere(leftFoot.position + leftFoot.rotation * new Vector3(LeftFootOffset.x, -LeftFootOffset.y, LeftFootOffset.z), 0.1f);
        Gizmos.DrawWireSphere(rightFoot.position + rightFoot.rotation * new Vector3(RightFootOffset.x, -RightFootOffset.y, RightFootOffset.z), 0.1f);
    }

    public void SetFPS(int fps)
    {
        Time.fixedDeltaTime = 1 / (float)fps;
    }

    public void SetEndAction(Action endAction)
    {
        this.endAction = endAction;
    }

    public void Stop()
    {
        IsPlaying = false;
        IsEnd = true;
        Animator = GetComponent<Animator>();
        Animator.enabled = true;
    }

    public void Pause()
    {
        IsPlaying = false;
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
    public void Play(VMDReader vmdReader)
    {
        Animator = GetComponent<Animator>();
        Animator.enabled = false;

        originalParentLocalPosition = transform.localPosition;
        originalParentLocalRotation = transform.localRotation;

        //モデルに初期ポーズを取らせる
        foreach (VMDReader.BoneKeyFrameGroup.BoneNames boneName in upperBoneOriginalPositions.Keys)
        {
            upperBoneTransformDictionary[boneName].Item1.localPosition = upperBoneOriginalPositions[boneName];
            upperBoneTransformDictionary[boneName].Item1.localRotation = Quaternion.identity;
        }

        //frame = 0において初期化
        centerIK = null;
        leftFootIK = null;
        rightFootIK = null;
        centerIK = new CenterAnimation(vmdReader, Animator);
        AnimateUpperBody(frameNumber);
        leftFootIK = new FootIK(vmdReader, Animator, FootIK.Feet.LeftFoot, LeftFootOffset);
        rightFootIK = new FootIK(vmdReader, Animator, FootIK.Feet.RightFoot, RightFootOffset);
        AnimateUpperBody(frameNumber);

        Play();
    }

    public void Play(string filePath)
    {
        frameNumber = 0;
        vmdReader = new VMDReader(filePath);

        Play(vmdReader);
    }

    public void Play(string filePath, Action endAction)
    {
        this.endAction = endAction;
        Play(filePath);
    }

    public void Play(string filePath, bool useParentOfAll)
    {
        useParentOfAll = UseParentOfAll;
        Play(filePath);
    }

    public void Play(string filePath, bool useParentOfAll, Action endAction)
    {
        this.endAction = endAction;
        Play(filePath, useParentOfAll);
    }

    public void JumpToFrame(int frameNumber)
    {
        this.frameNumber = frameNumber;
        AnimateUpperBody(frameNumber);
        InterpolateUpperBody(frameNumber);
        if (centerIK != null) { centerIK.Animate(frameNumber); }
        if (centerIK != null) { centerIK.Interpolate(frameNumber); }
        if (centerIK != null) { centerIK.Complement(frameNumber); }
        bool leftFootIKExists = false;
        bool rightFootIKExists = false;
        if (leftFootIK != null) { leftFootIKExists = leftFootIK.IK(frameNumber); }
        if (rightFootIK != null) { rightFootIKExists = rightFootIK.IK(frameNumber); }
        if (UseLegRotationBeta)
        {
            if (leftFootIKExists) { AnimateLowerBody(frameNumber, leftLowerBoneTransformDictionary); }
            if (rightFootIKExists) { AnimateLowerBody(frameNumber, rightLowerBoneTransformDictionary); }
        }
        if (leftFootIK != null) { leftFootIK.InterpolateIK(frameNumber); }
        if (rightFootIK != null) { rightFootIK.InterpolateIK(frameNumber); }
        if (UseLegRotationBeta)
        {
            InterpolateLowerBody(frameNumber, leftLowerBoneTransformDictionary);
            InterpolateLowerBody(frameNumber, rightLowerBoneTransformDictionary);
        }
    }

    void AnimateParentOfAll()
    {
        VMD.BoneKeyFrame parentBoneFrame = vmdReader.GetBoneKeyFrame(VMDReader.BoneKeyFrameGroup.BoneNames.全ての親, frameNumber);
        if (parentBoneFrame == null) { parentBoneFrame = new VMD.BoneKeyFrame(); }
        if (parentBoneFrame.Position != Vector3.zero)
        {
            transform.localPosition = originalParentLocalPosition + parentBoneFrame.Position * ParentAmplifier;
        }
        if (parentBoneFrame.Rotation != ZeroQuaternion)
        {
            transform.localRotation = originalParentLocalRotation.PlusRotation(parentBoneFrame.Rotation);
        }
    }

    void InterpolateParentOfAll()
    {
        VMDReader.BoneKeyFrameGroup vmdBoneFrameGroup = vmdReader.GetBoneKeyFrameGroup(VMDReader.BoneKeyFrameGroup.BoneNames.全ての親);
        VMD.BoneKeyFrame lastPositionVMDBoneFrame = vmdBoneFrameGroup.LastPositionKeyFrame;
        VMD.BoneKeyFrame lastRotationVMDBoneFrame = vmdBoneFrameGroup.LastRotationKeyFrame;
        VMD.BoneKeyFrame nextPositionVMDBoneFrame = vmdBoneFrameGroup.NextPositionKeyFrame;
        VMD.BoneKeyFrame nextRotationVMDBoneFrame = vmdBoneFrameGroup.NextRotationKeyFrame;

        if (nextPositionVMDBoneFrame != null && lastPositionVMDBoneFrame != null)
        {
            float xInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, vmdBoneFrameGroup.LastPositionKeyFrame.FrameNumber, vmdBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
            float yInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, vmdBoneFrameGroup.LastPositionKeyFrame.FrameNumber, vmdBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
            float zInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, vmdBoneFrameGroup.LastPositionKeyFrame.FrameNumber, vmdBoneFrameGroup.NextPositionKeyFrame.FrameNumber);

            float xInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.x, nextPositionVMDBoneFrame.Position.x, xInterpolationRate);
            float yInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.y, nextPositionVMDBoneFrame.Position.y, yInterpolationRate);
            float zInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.z, nextPositionVMDBoneFrame.Position.z, zInterpolationRate);
            transform.localPosition = originalParentLocalPosition + new Vector3(xInterpolation, yInterpolation, zInterpolation) * ParentAmplifier;
        }

        if (nextRotationVMDBoneFrame != null && lastRotationVMDBoneFrame != null)
        {
            float rotationInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, vmdBoneFrameGroup.LastRotationKeyFrame.FrameNumber, vmdBoneFrameGroup.NextRotationKeyFrame.FrameNumber);
            transform.localRotation = originalParentLocalRotation.PlusRotation(Quaternion.Lerp(lastRotationVMDBoneFrame.Rotation, nextRotationVMDBoneFrame.Rotation, rotationInterpolationRate));
        }
    }

    void AnimateUpperBody(int frameNumber)
    {
        void animateBone(VMDReader.BoneKeyFrameGroup.BoneNames boneName)
        {
            Transform boneTransform = upperBoneTransformDictionary[boneName].Item1;
            if (boneTransform == null) { return; }
            VMD.BoneKeyFrame vmdBoneFrame = vmdReader.GetBoneKeyFrame(boneName, frameNumber);
            if (vmdBoneFrame == null) { return; }

            if (vmdBoneFrame.Position != Vector3.zero)
            {
                boneTransform.localPosition = upperBoneOriginalPositions[boneName] + vmdBoneFrame.Position * upperBoneTransformDictionary[boneName].Item2;
            }
            if (vmdBoneFrame.Rotation != ZeroQuaternion)
            {
                boneTransform.localRotation = upperBoneOriginalRotations[boneName].PlusRotation(vmdBoneFrame.Rotation);
            }
        }

        foreach (VMDReader.BoneKeyFrameGroup.BoneNames boneName in upperBoneTransformDictionary.Keys) { animateBone(boneName); }
    }

    void InterpolateUpperBody(int frameNumber)
    {
        void interpolateBone(VMDReader.BoneKeyFrameGroup.BoneNames boneName)
        {
            Transform boneTransform = upperBoneTransformDictionary[boneName].Item1;

            VMDReader.BoneKeyFrameGroup vmdBoneFrameGroup = vmdReader.GetBoneKeyFrameGroup(boneName);
            VMD.BoneKeyFrame lastPositionVMDBoneFrame = vmdBoneFrameGroup.LastPositionKeyFrame;
            VMD.BoneKeyFrame lastRotationVMDBoneFrame = vmdBoneFrameGroup.LastRotationKeyFrame;
            VMD.BoneKeyFrame nextPositionVMDBoneFrame = vmdBoneFrameGroup.NextPositionKeyFrame;
            VMD.BoneKeyFrame nextRotationVMDBoneFrame = vmdBoneFrameGroup.NextRotationKeyFrame;

            if (boneTransform == null) { return; }

            if (nextPositionVMDBoneFrame != null && lastPositionVMDBoneFrame != null)
            {
                float xInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);
                float yInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);
                float zInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);

                float xInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.x, nextPositionVMDBoneFrame.Position.x, xInterpolationRate);
                float yInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.y, nextPositionVMDBoneFrame.Position.y, yInterpolationRate);
                float zInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.z, nextPositionVMDBoneFrame.Position.z, zInterpolationRate);
                boneTransform.localPosition = upperBoneOriginalPositions[boneName] + new Vector3(xInterpolation, yInterpolation, zInterpolation) * DefaultBoneAmplifier;
            }

            if (nextRotationVMDBoneFrame != null && lastRotationVMDBoneFrame != null)
            {
                float rotationInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, lastRotationVMDBoneFrame.FrameNumber, nextRotationVMDBoneFrame.FrameNumber);
                boneTransform.localRotation = upperBoneOriginalRotations[boneName].PlusRotation(Quaternion.Lerp(lastRotationVMDBoneFrame.Rotation, nextRotationVMDBoneFrame.Rotation, rotationInterpolationRate));
            }
        }

        foreach (VMDReader.BoneKeyFrameGroup.BoneNames boneName in upperBoneTransformDictionary.Keys) { interpolateBone(boneName); }
    }

    //必ず足IKの後に呼び出すこと
    void AnimateLowerBody(int frameNumber, Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, (Transform, float)> lowerBodyDictionary)
    {
        bool isLeft = (lowerBodyDictionary == leftLowerBoneTransformDictionary);

        foreach (VMDReader.BoneKeyFrameGroup.BoneNames boneName in lowerBodyDictionary.Keys)
        {
            Transform boneTransform = lowerBodyDictionary[boneName].Item1;
            if (boneTransform == null) { return; }
            VMD.BoneKeyFrame vmdBoneFrame = vmdReader.GetBoneKeyFrame(boneName, frameNumber);
            if (vmdBoneFrame == null) { return; }

            if (vmdBoneFrame.Rotation != ZeroQuaternion)
            {
                Quaternion originalRotation = isLeft ? leftFootIK.BoneLocalRotationDictionary[boneTransform] : rightFootIK.BoneLocalRotationDictionary[boneTransform];
                //y軸回転以外は無視できる
                boneTransform.localRotation = originalRotation.PlusRotation(Quaternion.Euler(0, vmdBoneFrame.Rotation.y, 0));
            }
        }
    }

    //必ず足IKの後に呼び出すこと
    void InterpolateLowerBody(int frameNumber, Dictionary<VMDReader.BoneKeyFrameGroup.BoneNames, (Transform, float)> lowerBodyDictionary)
    {
        bool isLeft = (lowerBodyDictionary == leftLowerBoneTransformDictionary);

        foreach (VMDReader.BoneKeyFrameGroup.BoneNames boneName in lowerBodyDictionary.Keys)
        {
            Transform boneTransform = lowerBodyDictionary[boneName].Item1;

            VMDReader.BoneKeyFrameGroup vmdBoneFrameGroup = vmdReader.GetBoneKeyFrameGroup(boneName);
            VMD.BoneKeyFrame lastRotationVMDBoneFrame = vmdBoneFrameGroup.LastRotationKeyFrame;
            VMD.BoneKeyFrame nextRotationVMDBoneFrame = vmdBoneFrameGroup.NextRotationKeyFrame;

            if (boneTransform == null) { return; }

            if (nextRotationVMDBoneFrame != null && lastRotationVMDBoneFrame != null)
            {
                Quaternion originalRotation = isLeft ? leftFootIK.BoneLocalRotationDictionary[boneTransform] : rightFootIK.BoneLocalRotationDictionary[boneTransform];
                float rotationInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, lastRotationVMDBoneFrame.FrameNumber, nextRotationVMDBoneFrame.FrameNumber);
                Quaternion rotation = Quaternion.Lerp(lastRotationVMDBoneFrame.Rotation, nextRotationVMDBoneFrame.Rotation, rotationInterpolationRate);
                //y軸回転以外は無視できる
                Vector3 rotationVector = new Vector3(0, rotation.eulerAngles.y, 0);
                boneTransform.localRotation = originalRotation.PlusRotation(Quaternion.Euler(rotationVector));
            }
        }
    }

    //VMDではセンターはHipの差分のみの位置、回転情報を持つ
    //Unityにない下半身ボーンの処理もここで行う
    class CenterAnimation
    {
        //センターでは回転情報なしは0,0,0,0ではなく0,0,0,1である
        readonly Quaternion ZeroQuaternion = Quaternion.identity;

        VMDReader.BoneKeyFrameGroup.BoneNames centerBoneName = VMDReader.BoneKeyFrameGroup.BoneNames.センター;
        VMDReader.BoneKeyFrameGroup.BoneNames grooveBoneName = VMDReader.BoneKeyFrameGroup.BoneNames.グルーブ;

        public VMDReader VMDReader { get; private set; }

        public Animator Animator { get; private set; }
        Transform hips;
        Transform spine;
        Vector3 originalLocalPosition;
        Quaternion originalLocalRotation;

        public CenterAnimation(VMDReader vmdReader, Animator animator)
        {
            VMDReader = vmdReader;
            Animator = animator;
            hips = Animator.GetBoneTransform(HumanBodyBones.Hips);
            spine = Animator.GetBoneTransform(HumanBodyBones.Spine);
            originalLocalPosition = hips.localPosition;
            originalLocalRotation = hips.localRotation;
        }

        public void Animate(int frameNumber)
        {
            //センター、グルーブの処理を行う
            VMD.BoneKeyFrame centerVMDBoneFrame = VMDReader.GetBoneKeyFrame(centerBoneName, frameNumber);
            VMD.BoneKeyFrame grooveVMDBoneFrame = VMDReader.GetBoneKeyFrame(grooveBoneName, frameNumber);

            if (centerVMDBoneFrame == null && grooveVMDBoneFrame == null) { return; }
            else if (centerVMDBoneFrame != null && grooveVMDBoneFrame == null)
            {
                if (centerVMDBoneFrame.Position != Vector3.zero)
                {
                    hips.localPosition = originalLocalPosition + centerVMDBoneFrame.Position * CenterIKAmplifier;
                }
                if (centerVMDBoneFrame.Rotation != ZeroQuaternion)
                {
                    hips.localRotation = originalLocalRotation.PlusRotation(centerVMDBoneFrame.Rotation);
                }
            }
            else if (centerVMDBoneFrame == null && grooveVMDBoneFrame != null)
            {
                if (grooveVMDBoneFrame.Position != Vector3.zero)
                {
                    hips.localPosition = originalLocalPosition + grooveVMDBoneFrame.Position * CenterIKAmplifier;
                }
                if (grooveVMDBoneFrame.Rotation != ZeroQuaternion)
                {
                    hips.localRotation = originalLocalRotation.PlusRotation(grooveVMDBoneFrame.Rotation);
                }
            }
            else
            {
                if (centerVMDBoneFrame.Position != Vector3.zero && grooveVMDBoneFrame.Position != Vector3.zero)
                {
                    hips.localPosition = originalLocalPosition + centerVMDBoneFrame.Position * CenterIKAmplifier + grooveVMDBoneFrame.Position * CenterIKAmplifier;
                }
                else if (centerVMDBoneFrame.Position != Vector3.zero && grooveVMDBoneFrame.Position == Vector3.zero)
                {
                    hips.localPosition = originalLocalPosition + centerVMDBoneFrame.Position;
                }
                else if (centerVMDBoneFrame.Position == Vector3.zero && grooveVMDBoneFrame.Position != Vector3.zero)
                {
                    hips.localPosition = originalLocalPosition + grooveVMDBoneFrame.Position;
                }


                if (centerVMDBoneFrame.Rotation != ZeroQuaternion && grooveVMDBoneFrame.Rotation != ZeroQuaternion)
                {
                    hips.localRotation = originalLocalRotation.PlusRotation(centerVMDBoneFrame.Rotation).PlusRotation(grooveVMDBoneFrame.Rotation);
                }
                else if (centerVMDBoneFrame.Rotation != ZeroQuaternion && grooveVMDBoneFrame.Rotation == ZeroQuaternion)
                {
                    hips.localRotation = originalLocalRotation.PlusRotation(centerVMDBoneFrame.Rotation);
                }
                else if (centerVMDBoneFrame.Rotation == ZeroQuaternion && grooveVMDBoneFrame.Rotation != ZeroQuaternion)
                {
                    hips.localRotation = originalLocalRotation.PlusRotation(grooveVMDBoneFrame.Rotation);
                }
            }

        }

        public void Interpolate(int frameNumber)
        {
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

            bool canInterpolateCenterPosition = (centerNextPositionVMDBoneFrame != null && centerLastPositionVMDBoneFrame != null);
            bool canInterpolateCenterRotation = (centerNextRotationVMDBoneFrame != null && centerLastRotationVMDBoneFrame != null);
            bool canInterpolateGroovePosition = (grooveNextPositionVMDBoneFrame != null && grooveLastPositionVMDBoneFrame != null);
            bool canInterpolateGrooveRotation = (grooveNextRotationVMDBoneFrame != null && grooveLastRotationVMDBoneFrame != null);

            if (canInterpolateCenterPosition && canInterpolateGroovePosition)
            {
                hips.localPosition = originalLocalPosition;

                float xCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, centerVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                float yCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, centerVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                float zCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, centerVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                float xCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.x, centerNextPositionVMDBoneFrame.Position.x, xCenterInterpolationRate);
                float yCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.y, centerNextPositionVMDBoneFrame.Position.y, yCenterInterpolationRate);
                float zCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.z, centerNextPositionVMDBoneFrame.Position.z, zCenterInterpolationRate);
                hips.localPosition += new Vector3(xCenterInterpolation, yCenterInterpolation, zCenterInterpolation) * CenterIKAmplifier;

                float xGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, grooveVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                float yGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, grooveVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                float zGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, grooveVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                float xGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.x, grooveNextPositionVMDBoneFrame.Position.x, xGrooveInterpolationRate);
                float yGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.y, grooveNextPositionVMDBoneFrame.Position.y, yGrooveInterpolationRate);
                float zGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.z, grooveNextPositionVMDBoneFrame.Position.z, zGrooveInterpolationRate);
                hips.localPosition += new Vector3(xGrooveInterpolation, yGrooveInterpolation, zGrooveInterpolation) * CenterIKAmplifier;
            }
            else if (canInterpolateCenterPosition && !canInterpolateGroovePosition)
            {
                hips.localPosition = originalLocalPosition;
                float xCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, centerVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                float yCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, centerVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                float zCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, centerVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                float xCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.x, centerNextPositionVMDBoneFrame.Position.x, xCenterInterpolationRate);
                float yCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.y, centerNextPositionVMDBoneFrame.Position.y, yCenterInterpolationRate);
                float zCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.z, centerNextPositionVMDBoneFrame.Position.z, zCenterInterpolationRate);
                hips.localPosition += new Vector3(xCenterInterpolation, yCenterInterpolation, zCenterInterpolation) * CenterIKAmplifier;
            }
            else if (!canInterpolateCenterPosition && canInterpolateGroovePosition)
            {
                hips.localPosition = originalLocalPosition;
                float xGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, grooveVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                float yGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, grooveVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                float zGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, grooveVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                float xGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.x, grooveNextPositionVMDBoneFrame.Position.x, xGrooveInterpolationRate);
                float yGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.y, grooveNextPositionVMDBoneFrame.Position.y, yGrooveInterpolationRate);
                float zGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.z, grooveNextPositionVMDBoneFrame.Position.z, zGrooveInterpolationRate);
                hips.localPosition += new Vector3(xGrooveInterpolation, yGrooveInterpolation, zGrooveInterpolation) * CenterIKAmplifier;
            }

            if (canInterpolateCenterRotation && canInterpolateGrooveRotation)
            {
                hips.localRotation = originalLocalRotation;
                float centerRotationInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, centerVMDBoneFrameGroup.LastRotationKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextRotationKeyFrame.FrameNumber);
                hips.localRotation = hips.localRotation.PlusRotation(Quaternion.Lerp(centerLastRotationVMDBoneFrame.Rotation, centerNextRotationVMDBoneFrame.Rotation, centerRotationInterpolationRate));
                float grooveRotationInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, grooveVMDBoneFrameGroup.LastRotationKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextRotationKeyFrame.FrameNumber);
                hips.localRotation = hips.localRotation.PlusRotation(Quaternion.Lerp(grooveLastRotationVMDBoneFrame.Rotation, grooveNextRotationVMDBoneFrame.Rotation, grooveRotationInterpolationRate));
            }
            else if (canInterpolateCenterRotation && !canInterpolateGrooveRotation)
            {
                hips.localRotation = originalLocalRotation;
                float centerRotationInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, centerVMDBoneFrameGroup.LastRotationKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextRotationKeyFrame.FrameNumber);
                hips.localRotation = hips.localRotation.PlusRotation(Quaternion.Lerp(centerLastRotationVMDBoneFrame.Rotation, centerNextRotationVMDBoneFrame.Rotation, centerRotationInterpolationRate));
            }
            else if (!canInterpolateCenterRotation && canInterpolateGrooveRotation)
            {
                hips.localRotation = originalLocalRotation;
                float grooveRotationInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, grooveVMDBoneFrameGroup.LastRotationKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextRotationKeyFrame.FrameNumber);
                hips.localRotation = hips.localRotation.PlusRotation(Quaternion.Lerp(grooveLastRotationVMDBoneFrame.Rotation, grooveNextRotationVMDBoneFrame.Rotation, grooveRotationInterpolationRate));
            }
        }

        //下半身の処理を行う
        public void Complement(int frameNumber)
        {
            //次に下半身の処理を行う、おそらく下半身に位置情報はないが一応位置情報の更新も行う
            VMDReader.BoneKeyFrameGroup.BoneNames lowerBodyBoneName = VMDReader.BoneKeyFrameGroup.BoneNames.下半身;
            if (hips == null) { return; }
            VMD.BoneKeyFrame lowerBodyVMDBoneFrame = VMDReader.GetBoneKeyFrame(lowerBodyBoneName, frameNumber);
            if (lowerBodyVMDBoneFrame != null)
            {
                hips.localPosition += lowerBodyVMDBoneFrame.Position * DefaultBoneAmplifier;
                spine.localPosition -= lowerBodyVMDBoneFrame.Position * DefaultBoneAmplifier;
                hips.localRotation = hips.localRotation.PlusRotation(lowerBodyVMDBoneFrame.Rotation);
                spine.localRotation = spine.localRotation.MinusRotation(lowerBodyVMDBoneFrame.Rotation);
            }
            else
            {
                VMDReader.BoneKeyFrameGroup lowerBodyVMDBoneGroup = VMDReader.GetBoneKeyFrameGroup(lowerBodyBoneName);
                if (lowerBodyVMDBoneGroup == null) { return; }
                VMD.BoneKeyFrame lastPositionVMDBoneFrame = lowerBodyVMDBoneGroup.LastPositionKeyFrame;
                VMD.BoneKeyFrame lastRotationVMDBoneFrame = lowerBodyVMDBoneGroup.LastRotationKeyFrame;
                VMD.BoneKeyFrame nextPositionVMDBoneFrame = lowerBodyVMDBoneGroup.NextPositionKeyFrame;
                VMD.BoneKeyFrame nextRotationVMDBoneFrame = lowerBodyVMDBoneGroup.NextRotationKeyFrame;

                if (nextPositionVMDBoneFrame != null && lastPositionVMDBoneFrame != null)
                {
                    float xInterpolationRate = lowerBodyVMDBoneGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);
                    float yInterpolationRate = lowerBodyVMDBoneGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);
                    float zInterpolationRate = lowerBodyVMDBoneGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);

                    float xInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.x, nextPositionVMDBoneFrame.Position.x, xInterpolationRate);
                    float yInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.y, nextPositionVMDBoneFrame.Position.y, yInterpolationRate);
                    float zInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.z, nextPositionVMDBoneFrame.Position.z, zInterpolationRate);

                    Vector3 deltaVector = new Vector3(xInterpolation, yInterpolation, zInterpolation) * DefaultBoneAmplifier;
                    hips.localPosition += deltaVector;
                    spine.localPosition -= deltaVector;
                }

                if (nextRotationVMDBoneFrame != null && lastRotationVMDBoneFrame != null)
                {
                    float rotationInterpolationRate = lowerBodyVMDBoneGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, lastRotationVMDBoneFrame.FrameNumber, nextRotationVMDBoneFrame.FrameNumber);

                    Quaternion deltaQuaternion = Quaternion.Lerp(lastRotationVMDBoneFrame.Rotation, nextRotationVMDBoneFrame.Rotation, rotationInterpolationRate);
                    hips.localRotation = hips.localRotation.PlusRotation(deltaQuaternion);
                    spine.localRotation = spine.localRotation.MinusRotation(deltaQuaternion);
                }
            }
        }
    }

    //VMDでは足IKはFootの差分のみの位置、回転情報を持つ
    //また、このコードで足先IKは未実装である
    class FootIK
    {
        readonly Quaternion ZeroQuaternion = new Quaternion(0, 0, 0, 0);

        public enum Feet { LeftFoot, RightFoot }

        const string ToeString = "Toe";

        public VMDReader VMDReader { get; private set; }

        public bool Enable { get; private set; } = true;
        public Feet Foot { get; private set; }
        public Animator Animator { get; private set; }
        public Vector3 Offset { get; private set; }
        public Transform HipTransform { get; private set; }
        public Transform KneeTransform { get; private set; }
        public Transform FootTransform { get; private set; }
        public Transform ToeTransform { get; private set; }

        public Dictionary<Transform, Quaternion> BoneLocalRotationDictionary { get; private set; }
        public Transform Target { get; private set; }
        private VMDReader.BoneKeyFrameGroup.BoneNames footBoneName { get; set; }
        private VMDReader.BoneKeyFrameGroup.BoneNames toeBoneName { get; set; }
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
                footBoneName = VMDReader.BoneKeyFrameGroup.BoneNames.左足ＩＫ;
                toeBoneName = VMDReader.BoneKeyFrameGroup.BoneNames.左つま先ＩＫ;
                HipTransform = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                KneeTransform = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                FootTransform = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            }
            else
            {
                footBoneName = VMDReader.BoneKeyFrameGroup.BoneNames.右足ＩＫ;
                toeBoneName = VMDReader.BoneKeyFrameGroup.BoneNames.右つま先ＩＫ;
                HipTransform = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                KneeTransform = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                FootTransform = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
            foreach (Transform childTransform in FootTransform)
            {
                if (childTransform.name.Contains(ToeString))
                {
                    ToeTransform = childTransform;
                    break;
                }
            }
            upperLegLength = Vector3.Distance(HipTransform.position, KneeTransform.position);
            lowerLegLength = Vector3.Distance(KneeTransform.position, FootTransform.position);
            legLength = upperLegLength + lowerLegLength;

            firstLocalPosition = FootTransform.position - Animator.transform.position;

            BoneLocalRotationDictionary = new Dictionary<Transform, Quaternion>()
            {
                { HipTransform, HipTransform.localRotation },
                { KneeTransform, KneeTransform.localRotation },
                { FootTransform, FootTransform.localRotation }
            };
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

            BoneLocalRotationDictionary[HipTransform] = HipTransform.localRotation;
            BoneLocalRotationDictionary[KneeTransform] = KneeTransform.localRotation;
            BoneLocalRotationDictionary[FootTransform] = FootTransform.localRotation;
        }
        public bool IK(int frameNumber)
        {
            SetIKEnable(frameNumber);

            if (!Enable) { return false; }

            VMD.BoneKeyFrame footIKFrame = VMDReader.GetBoneKeyFrame(footBoneName, frameNumber);

            if (footIKFrame == null || footIKFrame.Position == Vector3.zero) { return false; }

            FootTransform.localRotation = Quaternion.identity;

            Vector3 moveVector = footIKFrame.Position;

            Target.localPosition = firstLocalPosition + (moveVector * FootIKAmplifier) + Offset;

            IK();

            #region 足首
            //if (footIKFrame.Rotation == ZeroQuaternion) { return true; }
            //FootTransform.localRotation = footIKFrame.Rotation;

            ////つま先IK
            //VMD.BoneKeyFrame toeIKFrame = VMDReader.GetBoneKeyFrame(toeBoneName, frameNumber);
            //if (toeIKFrame == null) { return true; }
            //if (toeIKFrame.Rotation == ZeroQuaternion) { return true; }
            //Vector3 toeVector = ToeTransform.position - FootTransform.position;
            //Vector3 toeTargetVector = toeVector + toeIKFrame.Position * FootIKAmplifier;
            //FootTransform.localRotation = FootTransform.localRotation * Quaternion.FromToRotation(toeVector, toeTargetVector);
            #endregion 足首

            return true;
        }

        public void InterpolateIK(int frameNumber)
        {
            SetIKEnable(frameNumber);

            if (!Enable) { return; }

            FootTransform.localRotation = Quaternion.identity;

            VMDReader.BoneKeyFrameGroup vmdFootBoneFrameGroup = VMDReader.GetBoneKeyFrameGroup(footBoneName);
            VMD.BoneKeyFrame lastPositionFootVMDBoneFrame = vmdFootBoneFrameGroup.LastPositionKeyFrame;
            VMD.BoneKeyFrame lastRotationFootVMDBoneFrame = vmdFootBoneFrameGroup.LastRotationKeyFrame;
            VMD.BoneKeyFrame nextPositionFootVMDBoneFrame = vmdFootBoneFrameGroup.NextPositionKeyFrame;
            VMD.BoneKeyFrame nextRotationFootVMDBoneFrame = vmdFootBoneFrameGroup.NextRotationKeyFrame;

            if (nextPositionFootVMDBoneFrame != null && lastPositionFootVMDBoneFrame != null)
            {
                float xInterpolationRate = vmdFootBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, lastPositionFootVMDBoneFrame.FrameNumber, nextPositionFootVMDBoneFrame.FrameNumber);
                float yInterpolationRate = vmdFootBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, lastPositionFootVMDBoneFrame.FrameNumber, nextPositionFootVMDBoneFrame.FrameNumber);
                float zInterpolationRate = vmdFootBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, lastPositionFootVMDBoneFrame.FrameNumber, nextPositionFootVMDBoneFrame.FrameNumber);

                float xInterpolation = Mathf.Lerp(lastPositionFootVMDBoneFrame.Position.x, nextPositionFootVMDBoneFrame.Position.x, xInterpolationRate);
                float yInterpolation = Mathf.Lerp(lastPositionFootVMDBoneFrame.Position.y, nextPositionFootVMDBoneFrame.Position.y, yInterpolationRate);
                float zInterpolation = Mathf.Lerp(lastPositionFootVMDBoneFrame.Position.z, nextPositionFootVMDBoneFrame.Position.z, zInterpolationRate);

                Vector3 moveVector = new Vector3(xInterpolation, yInterpolation, zInterpolation);
                Target.localPosition = firstLocalPosition + (moveVector * FootIKAmplifier) + Offset;
            }

            IK();

            #region 足首
            //if (nextRotationFootVMDBoneFrame != null && lastRotationFootVMDBoneFrame != null)
            //{
            //    float rotationInterpolationRate = vmdFootBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, vmdFootBoneFrameGroup.LastRotationKeyFrame.FrameNumber, vmdFootBoneFrameGroup.NextRotationKeyFrame.FrameNumber);
            //    FootTransform.localRotation = Quaternion.Lerp(lastRotationFootVMDBoneFrame.Rotation, nextRotationFootVMDBoneFrame.Rotation, rotationInterpolationRate);
            //}

            ////つま先IK
            //VMDReader.BoneKeyFrameGroup vmdToeBoneFrameGroup = VMDReader.GetBoneKeyFrameGroup(footBoneName);
            //VMD.BoneKeyFrame lastPositionToeVMDBoneFrame = vmdToeBoneFrameGroup.LastPositionKeyFrame;
            //VMD.BoneKeyFrame nextPositionToeVMDBoneFrame = vmdToeBoneFrameGroup.NextPositionKeyFrame;
            //if (nextPositionToeVMDBoneFrame != null && lastPositionToeVMDBoneFrame != null)
            //{
            //    float xInterpolationRate = vmdToeBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, lastPositionToeVMDBoneFrame.FrameNumber, nextPositionToeVMDBoneFrame.FrameNumber);
            //    float yInterpolationRate = vmdToeBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, lastPositionToeVMDBoneFrame.FrameNumber, nextPositionToeVMDBoneFrame.FrameNumber);
            //    float zInterpolationRate = vmdToeBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, lastPositionToeVMDBoneFrame.FrameNumber, nextPositionToeVMDBoneFrame.FrameNumber);

            //    float xInterpolation = Mathf.Lerp(lastPositionToeVMDBoneFrame.Position.x, nextPositionToeVMDBoneFrame.Position.x, xInterpolationRate);
            //    float yInterpolation = Mathf.Lerp(lastPositionToeVMDBoneFrame.Position.y, nextPositionToeVMDBoneFrame.Position.y, yInterpolationRate);
            //    float zInterpolation = Mathf.Lerp(lastPositionToeVMDBoneFrame.Position.z, nextPositionToeVMDBoneFrame.Position.z, zInterpolationRate);

            //    Vector3 toeVector = ToeTransform.position - FootTransform.position;
            //    Vector3 toeTargetVector = toeVector + new Vector3(xInterpolation, yInterpolation, zInterpolation) * FootIKAmplifier;
            //    FootTransform.localRotation = FootTransform.localRotation * Quaternion.FromToRotation(toeVector, toeTargetVector);
            //}
            #endregion 足首
        }

        //内部でIKのEnableの値を設定している
        private void SetIKEnable(int frame)
        {
            VMD.IKKeyFrame currentIKFrame = VMDReader.RawVMD.IKFrames.Find(x => x.Frame == frame);
            if (currentIKFrame != null)
            {
                VMD.IKKeyFrame.VMDIKEnable currentIKEnable = currentIKFrame.IKEnable.Find((VMD.IKKeyFrame.VMDIKEnable x) => x.IKName == footBoneName.ToString());
                if (currentIKEnable != null)
                {
                    Enable = currentIKEnable.Enable;
                }
            }
        }
    }
}