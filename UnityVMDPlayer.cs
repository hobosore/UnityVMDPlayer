﻿using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using UnityVMDReader;
using static UnityVMDReader.VMDReader.BoneKeyFrameGroup;

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
    //エラー値の再現に用いる
    readonly Quaternion ZeroQuaternion = new Quaternion(0, 0, 0, 0);

    //以下はStart時に初期化
    //animatorはPlay時にも一応初期化
    public Animator Animator { get; private set; }
    //モデルの初期ポーズを保存
    Dictionary<BoneNames, Transform> boneTransformDictionary;
    Dictionary<BoneNames, (Transform, float)> upperBoneTransformDictionary;
    Dictionary<BoneNames, Vector3> upperBoneOriginalPositions;
    Dictionary<BoneNames, Quaternion> upperBoneOriginalRotations;
    Dictionary<BoneNames, (Transform, float)> leftLowerBoneTransformDictionary;
    Dictionary<BoneNames, Vector3> leftLowerBoneOriginalPositions;
    Dictionary<BoneNames, Quaternion> leftLowerBoneOriginalRotations;
    Dictionary<BoneNames, (Transform, float)> rightLowerBoneTransformDictionary;
    Dictionary<BoneNames, Vector3> rightLowerBoneOriginalPositions;
    Dictionary<BoneNames, Quaternion> rightLowerBoneOriginalRotations;

    //以下はPlay時に初期化
    int startedTime;
    Vector3 originalParentLocalPosition;
    Quaternion originalParentLocalRotation;
    FootIK leftFootIK;
    FootIK rightFootIK;
    CenterAnimation centerIK;
    VMDReader vmdReader;
    BoneGhost boneGhost;

    //以下はインスペクタにて設定
    public Transform LeftUpperArmTwist;
    public Transform RightUpperArmTwist;
    //VMDファイルのパスを与えて再生するまでオフセットは更新されない
    public Vector3 LeftFootOffset = new Vector3(0, 0, 0);
    public Vector3 RightFootOffset = new Vector3(0, 0, 0);

    // Start is called before the first frame update
    void Start()
    {
        Time.fixedDeltaTime = FPSs;

        Animator = GetComponent<Animator>();

        boneTransformDictionary = new Dictionary<BoneNames, Transform>()
            {
                //下半身などというものはUnityにはない
                { BoneNames.全ての親, (Animator.transform) },
                { BoneNames.センター, (Animator.GetBoneTransform(HumanBodyBones.Hips))},
                { BoneNames.上半身,   (Animator.GetBoneTransform(HumanBodyBones.Spine))},
                { BoneNames.上半身2,  (Animator.GetBoneTransform(HumanBodyBones.Chest))},
                { BoneNames.頭,       (Animator.GetBoneTransform(HumanBodyBones.Head))},
                { BoneNames.首,       (Animator.GetBoneTransform(HumanBodyBones.Neck))},
                { BoneNames.左肩,     (Animator.GetBoneTransform(HumanBodyBones.LeftShoulder))},
                { BoneNames.右肩,     (Animator.GetBoneTransform(HumanBodyBones.RightShoulder))},
                { BoneNames.左腕,     (Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm))},
                { BoneNames.右腕,     (Animator.GetBoneTransform(HumanBodyBones.RightUpperArm))},
                { BoneNames.左ひじ,   (Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm))},
                { BoneNames.右ひじ,   (Animator.GetBoneTransform(HumanBodyBones.RightLowerArm))},
                { BoneNames.左手首,   (Animator.GetBoneTransform(HumanBodyBones.LeftHand))},
                { BoneNames.右手首,   (Animator.GetBoneTransform(HumanBodyBones.RightHand))},
                { BoneNames.左親指１, (Animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal))},
                { BoneNames.右親指１, (Animator.GetBoneTransform(HumanBodyBones.RightThumbProximal))},
                { BoneNames.左親指２, (Animator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate))},
                { BoneNames.右親指２, (Animator.GetBoneTransform(HumanBodyBones.RightThumbIntermediate))},
                { BoneNames.左人指１, (Animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal))},
                { BoneNames.右人指１, (Animator.GetBoneTransform(HumanBodyBones.RightIndexProximal))},
                { BoneNames.左人指２, (Animator.GetBoneTransform(HumanBodyBones.LeftIndexIntermediate))},
                { BoneNames.右人指２, (Animator.GetBoneTransform(HumanBodyBones.RightIndexIntermediate))},
                { BoneNames.左人指３, (Animator.GetBoneTransform(HumanBodyBones.LeftIndexDistal))},
                { BoneNames.右人指３, (Animator.GetBoneTransform(HumanBodyBones.RightIndexDistal))},
                { BoneNames.左中指１, (Animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal))},
                { BoneNames.右中指１, (Animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal))},
                { BoneNames.左中指２, (Animator.GetBoneTransform(HumanBodyBones.LeftMiddleIntermediate))},
                { BoneNames.右中指２, (Animator.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate))},
                { BoneNames.左中指３, (Animator.GetBoneTransform(HumanBodyBones.LeftMiddleDistal))},
                { BoneNames.右中指３, (Animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal))},
                { BoneNames.左薬指１, (Animator.GetBoneTransform(HumanBodyBones.LeftRingProximal))},
                { BoneNames.右薬指１, (Animator.GetBoneTransform(HumanBodyBones.RightRingProximal))},
                { BoneNames.左薬指２, (Animator.GetBoneTransform(HumanBodyBones.LeftRingIntermediate))},
                { BoneNames.右薬指２, (Animator.GetBoneTransform(HumanBodyBones.RightRingIntermediate))},
                { BoneNames.左薬指３, (Animator.GetBoneTransform(HumanBodyBones.LeftRingDistal))},
                { BoneNames.右薬指３, (Animator.GetBoneTransform(HumanBodyBones.RightRingDistal))},
                { BoneNames.左小指１, (Animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal))},
                { BoneNames.右小指１, (Animator.GetBoneTransform(HumanBodyBones.RightLittleProximal))},
                { BoneNames.左小指２, (Animator.GetBoneTransform(HumanBodyBones.LeftLittleIntermediate))},
                { BoneNames.右小指２, (Animator.GetBoneTransform(HumanBodyBones.RightLittleIntermediate))},
                { BoneNames.左小指３, (Animator.GetBoneTransform(HumanBodyBones.LeftLittleDistal))},
                { BoneNames.右小指３, (Animator.GetBoneTransform(HumanBodyBones.RightLittleDistal))},
                { BoneNames.左足ＩＫ, (Animator.GetBoneTransform(HumanBodyBones.LeftFoot))},
                { BoneNames.右足ＩＫ, (Animator.GetBoneTransform(HumanBodyBones.RightFoot))},
                { BoneNames.左足,     (Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg))},
                { BoneNames.右足,     (Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg))},
                { BoneNames.左ひざ,   (Animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg))},
                { BoneNames.右ひざ,   (Animator.GetBoneTransform(HumanBodyBones.RightLowerLeg))},
                { BoneNames.左足首,   (Animator.GetBoneTransform(HumanBodyBones.LeftFoot))},
                { BoneNames.右足首,   (Animator.GetBoneTransform(HumanBodyBones.RightFoot))},
                //左つま先, 右つま先は情報付けると足首の回転、位置との矛盾が生じかねない
                //{ BoneNames.左つま先,   (animator.GetBoneTransform(HumanBodyBones.LeftToes))},
                //{ BoneNames.右つま先,   (animator.GetBoneTransform(HumanBodyBones.RightToes))}
        };
        upperBoneTransformDictionary = new Dictionary<BoneNames, (Transform, float)>()
        {
            //センターはHips
            //下半身などというものはUnityにはないので、センターとともに処理
            { BoneNames.上半身 ,   (Animator.GetBoneTransform(HumanBodyBones.Spine), DefaultBoneAmplifier) },
            { BoneNames.上半身2 ,  (Animator.GetBoneTransform(HumanBodyBones.Chest), DefaultBoneAmplifier) },
            { BoneNames.頭 ,       (Animator.GetBoneTransform(HumanBodyBones.Head), DefaultBoneAmplifier) },
            { BoneNames.首 ,       (Animator.GetBoneTransform(HumanBodyBones.Neck), DefaultBoneAmplifier) },
            { BoneNames.左肩 ,     (Animator.GetBoneTransform(HumanBodyBones.LeftShoulder), DefaultBoneAmplifier) },
            { BoneNames.右肩 ,     (Animator.GetBoneTransform(HumanBodyBones.RightShoulder), DefaultBoneAmplifier) },
            { BoneNames.左腕 ,     (Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), DefaultBoneAmplifier) },
            { BoneNames.右腕 ,     (Animator.GetBoneTransform(HumanBodyBones.RightUpperArm), DefaultBoneAmplifier) },
            { BoneNames.左ひじ ,   (Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), DefaultBoneAmplifier) },
            { BoneNames.右ひじ ,   (Animator.GetBoneTransform(HumanBodyBones.RightLowerArm), DefaultBoneAmplifier) },
            { BoneNames.左手首 ,   (Animator.GetBoneTransform(HumanBodyBones.LeftHand), DefaultBoneAmplifier) },
            { BoneNames.右手首 ,   (Animator.GetBoneTransform(HumanBodyBones.RightHand), DefaultBoneAmplifier) },
            { BoneNames.左つま先 , (Animator.GetBoneTransform(HumanBodyBones.LeftToes), DefaultBoneAmplifier) },
            { BoneNames.右つま先 , (Animator.GetBoneTransform(HumanBodyBones.RightToes), DefaultBoneAmplifier) },
            { BoneNames.左親指１ , (Animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal), DefaultBoneAmplifier) },
            { BoneNames.右親指１ , (Animator.GetBoneTransform(HumanBodyBones.RightThumbProximal), DefaultBoneAmplifier) },
            { BoneNames.左親指２ , (Animator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate), DefaultBoneAmplifier) },
            { BoneNames.右親指２ , (Animator.GetBoneTransform(HumanBodyBones.RightThumbIntermediate), DefaultBoneAmplifier) },
            { BoneNames.左人指１ , (Animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal), DefaultBoneAmplifier) },
            { BoneNames.右人指１ , (Animator.GetBoneTransform(HumanBodyBones.RightIndexProximal), DefaultBoneAmplifier) },
            { BoneNames.左人指２ , (Animator.GetBoneTransform(HumanBodyBones.LeftIndexIntermediate), DefaultBoneAmplifier) },
            { BoneNames.右人指２ , (Animator.GetBoneTransform(HumanBodyBones.RightIndexIntermediate), DefaultBoneAmplifier) },
            { BoneNames.左人指３ , (Animator.GetBoneTransform(HumanBodyBones.LeftIndexDistal), DefaultBoneAmplifier) },
            { BoneNames.右人指３ , (Animator.GetBoneTransform(HumanBodyBones.RightIndexDistal), DefaultBoneAmplifier) },
            { BoneNames.左中指１ , (Animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal), DefaultBoneAmplifier) },
            { BoneNames.右中指１ , (Animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal), DefaultBoneAmplifier) },
            { BoneNames.左中指２ , (Animator.GetBoneTransform(HumanBodyBones.LeftMiddleIntermediate), DefaultBoneAmplifier) },
            { BoneNames.右中指２ , (Animator.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate), DefaultBoneAmplifier) },
            { BoneNames.左中指３ , (Animator.GetBoneTransform(HumanBodyBones.LeftMiddleDistal), DefaultBoneAmplifier) },
            { BoneNames.右中指３ , (Animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal), DefaultBoneAmplifier) },
            { BoneNames.左薬指１ , (Animator.GetBoneTransform(HumanBodyBones.LeftRingProximal), DefaultBoneAmplifier) },
            { BoneNames.右薬指１ , (Animator.GetBoneTransform(HumanBodyBones.RightRingProximal), DefaultBoneAmplifier) },
            { BoneNames.左薬指２ , (Animator.GetBoneTransform(HumanBodyBones.LeftRingIntermediate), DefaultBoneAmplifier) },
            { BoneNames.右薬指２ , (Animator.GetBoneTransform(HumanBodyBones.RightRingIntermediate), DefaultBoneAmplifier) },
            { BoneNames.左薬指３ , (Animator.GetBoneTransform(HumanBodyBones.LeftRingDistal), DefaultBoneAmplifier) },
            { BoneNames.右薬指３ , (Animator.GetBoneTransform(HumanBodyBones.RightRingDistal), DefaultBoneAmplifier) },
            { BoneNames.左小指１ , (Animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal), DefaultBoneAmplifier) },
            { BoneNames.右小指１ , (Animator.GetBoneTransform(HumanBodyBones.RightLittleProximal), DefaultBoneAmplifier) },
            { BoneNames.左小指２ , (Animator.GetBoneTransform(HumanBodyBones.LeftLittleIntermediate), DefaultBoneAmplifier) },
            { BoneNames.右小指２ , (Animator.GetBoneTransform(HumanBodyBones.RightLittleIntermediate), DefaultBoneAmplifier) },
            { BoneNames.左小指３ , (Animator.GetBoneTransform(HumanBodyBones.LeftLittleDistal), DefaultBoneAmplifier) },
            { BoneNames.右小指３ , (Animator.GetBoneTransform(HumanBodyBones.RightLittleDistal), DefaultBoneAmplifier) },
        };
        if (LeftUpperArmTwist == null)
        {
            upperBoneTransformDictionary.Add(BoneNames.左腕捩れ, (LeftUpperArmTwist, DefaultBoneAmplifier));
        }
        if (RightUpperArmTwist == null)
        {
            upperBoneTransformDictionary.Add(BoneNames.右腕捩れ, (RightUpperArmTwist, DefaultBoneAmplifier));
        }
        leftLowerBoneTransformDictionary = new Dictionary<BoneNames, (Transform, float)>()
        {
            //下半身などというものはUnityにはないので、センターとともに処理
            { BoneNames.左足 ,     (Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), DefaultBoneAmplifier) },
            { BoneNames.左ひざ ,   (Animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), DefaultBoneAmplifier) },
            { BoneNames.左足首 ,   (Animator.GetBoneTransform(HumanBodyBones.LeftFoot), DefaultBoneAmplifier) },
        };
        rightLowerBoneTransformDictionary = new Dictionary<BoneNames, (Transform, float)>()
        {
            //下半身などというものはUnityにはないので、センターとともに処理
            { BoneNames.右足 ,     (Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg), DefaultBoneAmplifier) },
            { BoneNames.右ひざ ,   (Animator.GetBoneTransform(HumanBodyBones.RightLowerLeg), DefaultBoneAmplifier) },
            { BoneNames.右足首 ,   (Animator.GetBoneTransform(HumanBodyBones.RightFoot), DefaultBoneAmplifier) },
        };

        //モデルの初期ポーズを保存
        upperBoneOriginalPositions = new Dictionary<BoneNames, Vector3>();
        upperBoneOriginalRotations = new Dictionary<BoneNames, Quaternion>();
        int count = VMDReader.BoneKeyFrameGroup.StringBoneNames.Count;
        for (int i = 0; i < count; i++)
        {
            BoneNames boneName = (BoneNames)VMDReader.BoneKeyFrameGroup.StringBoneNames.IndexOf(VMDReader.BoneKeyFrameGroup.StringBoneNames[i]);
            if (!upperBoneTransformDictionary.Keys.Contains(boneName) || upperBoneTransformDictionary[boneName].Item1 == null) { continue; }
            upperBoneOriginalPositions.Add(boneName, upperBoneTransformDictionary[boneName].Item1.localPosition);
            upperBoneOriginalRotations.Add(boneName, upperBoneTransformDictionary[boneName].Item1.localRotation);
        }

        leftLowerBoneOriginalPositions = new Dictionary<BoneNames, Vector3>();
        leftLowerBoneOriginalRotations = new Dictionary<BoneNames, Quaternion>();
        for (int i = 0; i < count; i++)
        {
            BoneNames boneName = (BoneNames)VMDReader.BoneKeyFrameGroup.StringBoneNames.IndexOf(VMDReader.BoneKeyFrameGroup.StringBoneNames[i]);
            if (!leftLowerBoneTransformDictionary.Keys.Contains(boneName) || leftLowerBoneTransformDictionary[boneName].Item1 == null) { continue; }
            leftLowerBoneOriginalPositions.Add(boneName, leftLowerBoneTransformDictionary[boneName].Item1.localPosition);
            leftLowerBoneOriginalRotations.Add(boneName, leftLowerBoneTransformDictionary[boneName].Item1.localRotation);
        }

        rightLowerBoneOriginalPositions = new Dictionary<BoneNames, Vector3>();
        rightLowerBoneOriginalRotations = new Dictionary<BoneNames, Quaternion>();
        for (int i = 0; i < count; i++)
        {
            BoneNames boneName = (BoneNames)VMDReader.BoneKeyFrameGroup.StringBoneNames.IndexOf(VMDReader.BoneKeyFrameGroup.StringBoneNames[i]);
            if (!rightLowerBoneTransformDictionary.Keys.Contains(boneName) || rightLowerBoneTransformDictionary[boneName].Item1 == null) { continue; }
            rightLowerBoneOriginalPositions.Add(boneName, rightLowerBoneTransformDictionary[boneName].Item1.localPosition);
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

        //Ghost
        boneGhost.GhostAll();

        //足IKを使うかどうか
        if (leftFootIK != null) { boneGhost.SetLeftFootGhostEnable(!leftFootIK.Enable); }
        if (rightFootIK != null) { boneGhost.SetRightFootGhostEnable(!rightFootIK.Enable); }
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
        foreach (BoneNames boneName in upperBoneOriginalPositions.Keys)
        {
            upperBoneTransformDictionary[boneName].Item1.localPosition = upperBoneOriginalPositions[boneName];
            upperBoneTransformDictionary[boneName].Item1.localRotation = upperBoneOriginalRotations[boneName];
        }

        this.vmdReader = vmdReader;
        boneGhost = new BoneGhost(Animator, boneTransformDictionary); 

        //frame = 0において初期化
        centerIK = null;
        leftFootIK = null;
        rightFootIK = null;
        centerIK = new CenterAnimation(vmdReader, Animator, boneGhost);
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
        boneGhost.GhostAll(); 
    }

    void AnimateParentOfAll()
    {
        VMD.BoneKeyFrame parentBoneFrame = vmdReader.GetBoneKeyFrame(BoneNames.全ての親, frameNumber);
        if (parentBoneFrame == null) { parentBoneFrame = new VMD.BoneKeyFrame(); }
        if (parentBoneFrame.Position != Vector3.zero)
        {
            transform.localPosition = originalParentLocalPosition + parentBoneFrame.Position * DefaultBoneAmplifier;
        }
        if (parentBoneFrame.Rotation != ZeroQuaternion)
        {
            transform.localRotation = originalParentLocalRotation.PlusRotation(parentBoneFrame.Rotation);
        }
    }

    void InterpolateParentOfAll()
    {
        VMDReader.BoneKeyFrameGroup vmdBoneFrameGroup = vmdReader.GetBoneKeyFrameGroup(BoneNames.全ての親);
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
            transform.localPosition = originalParentLocalPosition + new Vector3(xInterpolation, yInterpolation, zInterpolation) * DefaultBoneAmplifier;
        }

        if (nextRotationVMDBoneFrame != null && lastRotationVMDBoneFrame != null)
        {
            float rotationInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, vmdBoneFrameGroup.LastRotationKeyFrame.FrameNumber, vmdBoneFrameGroup.NextRotationKeyFrame.FrameNumber);
            transform.localRotation = originalParentLocalRotation.PlusRotation(Quaternion.Lerp(lastRotationVMDBoneFrame.Rotation, nextRotationVMDBoneFrame.Rotation, rotationInterpolationRate));
        }
    }

    void AnimateUpperBody(int frameNumber)
    {
        foreach (BoneNames boneName in upperBoneTransformDictionary.Keys)
        {
            Transform boneTransform = upperBoneTransformDictionary[boneName].Item1;
            if (boneTransform == null) { continue; }

            VMD.BoneKeyFrame vmdBoneFrame = vmdReader.GetBoneKeyFrame(boneName, frameNumber);

            if (vmdBoneFrame == null) { continue; }

            if (boneGhost.GhostDictionary.Keys.Contains(boneName)
                && boneGhost.GhostDictionary[boneName].enabled
                && boneGhost.GhostDictionary[boneName].ghost != null)
            {
                if (vmdBoneFrame.Position != Vector3.zero)
                {
                    boneGhost.GhostDictionary[boneName].ghost.localPosition = boneGhost.OriginalGhostLocalPositionDictionary[boneName] + vmdBoneFrame.Position * upperBoneTransformDictionary[boneName].Item2;
                }
                if (vmdBoneFrame.Rotation != ZeroQuaternion)
                {
                    //Ghostは正規化されている
                    boneGhost.GhostDictionary[boneName].ghost.localRotation = Quaternion.identity.PlusRotation(vmdBoneFrame.Rotation);
                }
            }
        }
    }

    void InterpolateUpperBody(int frameNumber)
    {
        foreach (BoneNames boneName in upperBoneTransformDictionary.Keys)
        {
            Transform boneTransform = upperBoneTransformDictionary[boneName].Item1;
            if (boneTransform == null) { continue; }

            VMDReader.BoneKeyFrameGroup vmdBoneFrameGroup = vmdReader.GetBoneKeyFrameGroup(boneName);
            VMD.BoneKeyFrame lastPositionVMDBoneFrame = vmdBoneFrameGroup.LastPositionKeyFrame;
            VMD.BoneKeyFrame lastRotationVMDBoneFrame = vmdBoneFrameGroup.LastRotationKeyFrame;
            VMD.BoneKeyFrame nextPositionVMDBoneFrame = vmdBoneFrameGroup.NextPositionKeyFrame;
            VMD.BoneKeyFrame nextRotationVMDBoneFrame = vmdBoneFrameGroup.NextRotationKeyFrame;

            if (boneGhost.GhostDictionary.Keys.Contains(boneName)
                && boneGhost.GhostDictionary[boneName].enabled
                && boneGhost.GhostDictionary[boneName].ghost != null)
            {
                if (nextPositionVMDBoneFrame != null && lastPositionVMDBoneFrame != null)
                {
                    float xInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);
                    float yInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);
                    float zInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);

                    float xInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.x, nextPositionVMDBoneFrame.Position.x, xInterpolationRate);
                    float yInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.y, nextPositionVMDBoneFrame.Position.y, yInterpolationRate);
                    float zInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.z, nextPositionVMDBoneFrame.Position.z, zInterpolationRate);
                    boneGhost.GhostDictionary[boneName].ghost.localPosition = boneGhost.OriginalGhostLocalPositionDictionary[boneName] + new Vector3(xInterpolation, yInterpolation, zInterpolation) * DefaultBoneAmplifier;
                }

                if (nextRotationVMDBoneFrame != null && lastRotationVMDBoneFrame != null)
                {
                    float rotationInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, lastRotationVMDBoneFrame.FrameNumber, nextRotationVMDBoneFrame.FrameNumber);
                    //Ghostは正規化されている
                    boneGhost.GhostDictionary[boneName].ghost.localRotation = Quaternion.identity.PlusRotation(Quaternion.Lerp(lastRotationVMDBoneFrame.Rotation, nextRotationVMDBoneFrame.Rotation, rotationInterpolationRate));
                }
            }
        }
    }

    //必ず足IKの後に呼び出すこと
    void AnimateLowerBody(int frameNumber, Dictionary<BoneNames, (Transform, float)> lowerBodyDictionary)
    {
        bool isLeft = (lowerBodyDictionary == leftLowerBoneTransformDictionary);

        if (isLeft && !leftFootIK.Enable) { return; }
        if (!isLeft && !rightFootIK.Enable) { return; }

        foreach (BoneNames boneName in lowerBodyDictionary.Keys)
        {
            Transform boneTransform = lowerBodyDictionary[boneName].Item1;
            if (boneTransform == null) { return; }
            VMD.BoneKeyFrame vmdBoneFrame = vmdReader.GetBoneKeyFrame(boneName, frameNumber);
            if (vmdBoneFrame == null) { return; }

            if (boneGhost.GhostDictionary.Keys.Contains(boneName)
            && boneGhost.GhostDictionary[boneName].enabled
            && boneGhost.GhostDictionary[boneName].ghost != null)
            {
                if (vmdBoneFrame.Position != Vector3.zero)
                {
                    Vector3 originalPosition = boneGhost.OriginalGhostLocalPositionDictionary[boneName];
                    boneGhost.GhostDictionary[boneName].ghost.localPosition = upperBoneOriginalPositions[boneName] + vmdBoneFrame.Position * upperBoneTransformDictionary[boneName].Item2;
                }

                if (vmdBoneFrame.Rotation != ZeroQuaternion)
                {
                    //Ghostは正規化されている
                    Quaternion originalRotation = Quaternion.identity;
                    //y軸回転以外は無視できる
                    boneGhost.GhostDictionary[boneName].ghost.localRotation = originalRotation.PlusRotation(new Quaternion(-vmdBoneFrame.Rotation.x, vmdBoneFrame.Rotation.y, -vmdBoneFrame.Rotation.z, vmdBoneFrame.Rotation.w));
                }
            }
        }
    }

    //必ず足IKの後に呼び出すこと
    void InterpolateLowerBody(int frameNumber, Dictionary<BoneNames, (Transform, float)> lowerBodyDictionary)
    {
        bool isLeft = (lowerBodyDictionary == leftLowerBoneTransformDictionary);

        if (isLeft && !leftFootIK.Enable) { return; }
        if (!isLeft && !rightFootIK.Enable) { return; }

        foreach (BoneNames boneName in lowerBodyDictionary.Keys)
        {
            Transform boneTransform = lowerBodyDictionary[boneName].Item1;

            VMDReader.BoneKeyFrameGroup vmdBoneFrameGroup = vmdReader.GetBoneKeyFrameGroup(boneName);
            VMD.BoneKeyFrame lastPositionVMDBoneFrame = vmdBoneFrameGroup.LastPositionKeyFrame;
            VMD.BoneKeyFrame lastRotationVMDBoneFrame = vmdBoneFrameGroup.LastRotationKeyFrame;
            VMD.BoneKeyFrame nextPositionVMDBoneFrame = vmdBoneFrameGroup.NextPositionKeyFrame;
            VMD.BoneKeyFrame nextRotationVMDBoneFrame = vmdBoneFrameGroup.NextRotationKeyFrame;

            if (boneTransform == null) { return; }

            if (boneGhost.GhostDictionary.Keys.Contains(boneName)
            && boneGhost.GhostDictionary[boneName].enabled
            && boneGhost.GhostDictionary[boneName].ghost != null)
            {
                if (nextPositionVMDBoneFrame != null && lastPositionVMDBoneFrame != null)
                {
                    float xInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);
                    float yInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);
                    float zInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);

                    float xInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.x, nextPositionVMDBoneFrame.Position.x, xInterpolationRate);
                    float yInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.y, nextPositionVMDBoneFrame.Position.y, yInterpolationRate);
                    float zInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.z, nextPositionVMDBoneFrame.Position.z, zInterpolationRate);
                    Vector3 originalPosition = boneGhost.OriginalGhostLocalPositionDictionary[boneName];
                    boneGhost.GhostDictionary[boneName].ghost.localPosition = originalPosition + new Vector3(xInterpolation, yInterpolation, zInterpolation) * DefaultBoneAmplifier;
                }

                if (nextRotationVMDBoneFrame != null && lastRotationVMDBoneFrame != null)
                {
                    //Ghostは正規化されている
                    Quaternion originalRotation = Quaternion.identity;
                    float rotationInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, lastRotationVMDBoneFrame.FrameNumber, nextRotationVMDBoneFrame.FrameNumber);
                    Quaternion rotation = Quaternion.Lerp(lastRotationVMDBoneFrame.Rotation, nextRotationVMDBoneFrame.Rotation, rotationInterpolationRate);
                    boneGhost.GhostDictionary[boneName].ghost.localRotation = originalRotation.PlusRotation(new Quaternion(-rotation.x, rotation.y, -rotation.z, rotation.w));
                }
            }

            if (nextPositionVMDBoneFrame != null && lastPositionVMDBoneFrame != null)
            {
                float xInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);
                float yInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);
                float zInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, lastPositionVMDBoneFrame.FrameNumber, nextPositionVMDBoneFrame.FrameNumber);

                float xInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.x, nextPositionVMDBoneFrame.Position.x, xInterpolationRate);
                float yInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.y, nextPositionVMDBoneFrame.Position.y, yInterpolationRate);
                float zInterpolation = Mathf.Lerp(lastPositionVMDBoneFrame.Position.z, nextPositionVMDBoneFrame.Position.z, zInterpolationRate);
                Vector3 originalPosition = isLeft ? leftLowerBoneOriginalPositions[boneName] : rightLowerBoneOriginalPositions[boneName];
                boneTransform.localPosition = originalPosition + new Vector3(xInterpolation, yInterpolation, zInterpolation) * DefaultBoneAmplifier;
            }

            if (nextRotationVMDBoneFrame != null && lastRotationVMDBoneFrame != null)
            {
                Quaternion originalRotation = isLeft ? leftLowerBoneOriginalRotations[boneName] : rightLowerBoneOriginalRotations[boneName];
                float rotationInterpolationRate = vmdBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, lastRotationVMDBoneFrame.FrameNumber, nextRotationVMDBoneFrame.FrameNumber);
                Quaternion rotation = Quaternion.Lerp(lastRotationVMDBoneFrame.Rotation, nextRotationVMDBoneFrame.Rotation, rotationInterpolationRate);
                boneTransform.localRotation = originalRotation.PlusRotation(new Quaternion(-rotation.x, rotation.y, -rotation.z, rotation.w));
            }
        }
    }

    //VMDではセンターはHipの差分のみの位置、回転情報を持つ
    //Unityにない下半身ボーンの処理もここで行う
    class CenterAnimation
    {
        //センターでは回転情報なしは0,0,0,0ではなく0,0,0,1である
        readonly Quaternion ZeroQuaternion = Quaternion.identity;

        BoneNames centerBoneName = BoneNames.センター;
        BoneNames grooveBoneName = BoneNames.グルーブ;

        public VMDReader VMDReader { get; private set; }

        public Animator Animator { get; private set; }
        Transform hips;
        BoneGhost boneGhost;

        public CenterAnimation(VMDReader vmdReader, Animator animator, BoneGhost boneGhost)
        {
            VMDReader = vmdReader;
            Animator = animator;
            hips = Animator.GetBoneTransform(HumanBodyBones.Hips);
            this.boneGhost = boneGhost;
        }

        public void Animate(int frameNumber)
        {
            //センター、グルーブの処理を行う
            VMD.BoneKeyFrame centerVMDBoneFrame = VMDReader.GetBoneKeyFrame(centerBoneName, frameNumber);
            VMD.BoneKeyFrame grooveVMDBoneFrame = VMDReader.GetBoneKeyFrame(grooveBoneName, frameNumber);

            if (boneGhost.GhostDictionary.Keys.Contains(BoneNames.センター)
            && boneGhost.GhostDictionary[BoneNames.センター].enabled
            && boneGhost.GhostDictionary[BoneNames.センター].ghost != null)
            {
                if (centerVMDBoneFrame == null && grooveVMDBoneFrame == null) { return; }
                else if (centerVMDBoneFrame != null && grooveVMDBoneFrame == null)
                {
                    if (centerVMDBoneFrame.Position != Vector3.zero)
                    {
                        boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition = boneGhost.OriginalGhostLocalPositionDictionary[BoneNames.センター] + centerVMDBoneFrame.Position * DefaultBoneAmplifier;
                    }
                    if (centerVMDBoneFrame.Rotation != ZeroQuaternion)
                    {
                        //Ghostは正規化されている
                        boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = Quaternion.identity.PlusRotation(centerVMDBoneFrame.Rotation);
                    }
                }
                else if (centerVMDBoneFrame == null && grooveVMDBoneFrame != null)
                {
                    if (grooveVMDBoneFrame.Position != Vector3.zero)
                    {
                        boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition = boneGhost.OriginalGhostLocalPositionDictionary[BoneNames.センター] + grooveVMDBoneFrame.Position * DefaultBoneAmplifier;
                    }
                    if (grooveVMDBoneFrame.Rotation != ZeroQuaternion)
                    {
                        //Ghostは正規化されている
                        hips.localRotation = Quaternion.identity.PlusRotation(grooveVMDBoneFrame.Rotation);
                    }
                }
                else
                {
                    if (centerVMDBoneFrame.Position != Vector3.zero && grooveVMDBoneFrame.Position != Vector3.zero)
                    {
                        boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition = boneGhost.OriginalGhostLocalPositionDictionary[BoneNames.センター] + centerVMDBoneFrame.Position * DefaultBoneAmplifier + grooveVMDBoneFrame.Position * DefaultBoneAmplifier;
                    }
                    else if (centerVMDBoneFrame.Position != Vector3.zero && grooveVMDBoneFrame.Position == Vector3.zero)
                    {
                        boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition = boneGhost.OriginalGhostLocalPositionDictionary[BoneNames.センター] + centerVMDBoneFrame.Position;
                    }
                    else if (centerVMDBoneFrame.Position == Vector3.zero && grooveVMDBoneFrame.Position != Vector3.zero)
                    {
                        boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition = boneGhost.OriginalGhostLocalPositionDictionary[BoneNames.センター] + grooveVMDBoneFrame.Position;
                    }


                    if (centerVMDBoneFrame.Rotation != ZeroQuaternion && grooveVMDBoneFrame.Rotation != ZeroQuaternion)
                    {
                        //Ghostは正規化されている
                        boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = Quaternion.identity.PlusRotation(centerVMDBoneFrame.Rotation).PlusRotation(grooveVMDBoneFrame.Rotation);
                    }
                    else if (centerVMDBoneFrame.Rotation != ZeroQuaternion && grooveVMDBoneFrame.Rotation == ZeroQuaternion)
                    {
                        //Ghostは正規化されている
                        boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = Quaternion.identity.PlusRotation(centerVMDBoneFrame.Rotation);
                    }
                    else if (centerVMDBoneFrame.Rotation == ZeroQuaternion && grooveVMDBoneFrame.Rotation != ZeroQuaternion)
                    {
                        //Ghostは正規化されている
                        boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = Quaternion.identity.PlusRotation(grooveVMDBoneFrame.Rotation);
                    }
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

            if (boneGhost.GhostDictionary.Keys.Contains(BoneNames.センター)
            && boneGhost.GhostDictionary[BoneNames.センター].enabled
            && boneGhost.GhostDictionary[BoneNames.センター].ghost != null)
            {
                if (canInterpolateCenterPosition && canInterpolateGroovePosition)
                {
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition = boneGhost.OriginalGhostLocalPositionDictionary[BoneNames.センター];

                    float xCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, centerVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                    float yCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, centerVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                    float zCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, centerVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                    float xCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.x, centerNextPositionVMDBoneFrame.Position.x, xCenterInterpolationRate);
                    float yCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.y, centerNextPositionVMDBoneFrame.Position.y, yCenterInterpolationRate);
                    float zCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.z, centerNextPositionVMDBoneFrame.Position.z, zCenterInterpolationRate);
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition += new Vector3(xCenterInterpolation, yCenterInterpolation, zCenterInterpolation) * DefaultBoneAmplifier;

                    float xGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, grooveVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                    float yGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, grooveVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                    float zGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, grooveVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                    float xGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.x, grooveNextPositionVMDBoneFrame.Position.x, xGrooveInterpolationRate);
                    float yGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.y, grooveNextPositionVMDBoneFrame.Position.y, yGrooveInterpolationRate);
                    float zGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.z, grooveNextPositionVMDBoneFrame.Position.z, zGrooveInterpolationRate);
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition += new Vector3(xGrooveInterpolation, yGrooveInterpolation, zGrooveInterpolation) * DefaultBoneAmplifier;
                }
                else if (canInterpolateCenterPosition && !canInterpolateGroovePosition)
                {
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition = boneGhost.OriginalGhostLocalPositionDictionary[BoneNames.センター];
                    float xCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, centerVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                    float yCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, centerVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                    float zCenterInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, centerVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                    float xCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.x, centerNextPositionVMDBoneFrame.Position.x, xCenterInterpolationRate);
                    float yCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.y, centerNextPositionVMDBoneFrame.Position.y, yCenterInterpolationRate);
                    float zCenterInterpolation = Mathf.Lerp(centerLastPositionVMDBoneFrame.Position.z, centerNextPositionVMDBoneFrame.Position.z, zCenterInterpolationRate);
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition += new Vector3(xCenterInterpolation, yCenterInterpolation, zCenterInterpolation) * DefaultBoneAmplifier;
                }
                else if (!canInterpolateCenterPosition && canInterpolateGroovePosition)
                {
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition = boneGhost.OriginalGhostLocalPositionDictionary[BoneNames.センター];
                    float xGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, grooveVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                    float yGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, grooveVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                    float zGrooveInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, grooveVMDBoneFrameGroup.LastPositionKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextPositionKeyFrame.FrameNumber);
                    float xGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.x, grooveNextPositionVMDBoneFrame.Position.x, xGrooveInterpolationRate);
                    float yGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.y, grooveNextPositionVMDBoneFrame.Position.y, yGrooveInterpolationRate);
                    float zGrooveInterpolation = Mathf.Lerp(grooveLastPositionVMDBoneFrame.Position.z, grooveNextPositionVMDBoneFrame.Position.z, zGrooveInterpolationRate);
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition += new Vector3(xGrooveInterpolation, yGrooveInterpolation, zGrooveInterpolation) * DefaultBoneAmplifier;
                }

                if (canInterpolateCenterRotation && canInterpolateGrooveRotation)
                {
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = Quaternion.identity;
                    float centerRotationInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, centerVMDBoneFrameGroup.LastRotationKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextRotationKeyFrame.FrameNumber);
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation.PlusRotation(Quaternion.Lerp(centerLastRotationVMDBoneFrame.Rotation, centerNextRotationVMDBoneFrame.Rotation, centerRotationInterpolationRate));
                    float grooveRotationInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, grooveVMDBoneFrameGroup.LastRotationKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextRotationKeyFrame.FrameNumber);
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation.PlusRotation(Quaternion.Lerp(grooveLastRotationVMDBoneFrame.Rotation, grooveNextRotationVMDBoneFrame.Rotation, grooveRotationInterpolationRate));
                }
                else if (canInterpolateCenterRotation && !canInterpolateGrooveRotation)
                {
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = Quaternion.identity;
                    float centerRotationInterpolationRate = centerVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, centerVMDBoneFrameGroup.LastRotationKeyFrame.FrameNumber, centerVMDBoneFrameGroup.NextRotationKeyFrame.FrameNumber);
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation.PlusRotation(Quaternion.Lerp(centerLastRotationVMDBoneFrame.Rotation, centerNextRotationVMDBoneFrame.Rotation, centerRotationInterpolationRate));
                }
                else if (!canInterpolateCenterRotation && canInterpolateGrooveRotation)
                {
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = Quaternion.identity;
                    float grooveRotationInterpolationRate = grooveVMDBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, grooveVMDBoneFrameGroup.LastRotationKeyFrame.FrameNumber, grooveVMDBoneFrameGroup.NextRotationKeyFrame.FrameNumber);
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation.PlusRotation(Quaternion.Lerp(grooveLastRotationVMDBoneFrame.Rotation, grooveNextRotationVMDBoneFrame.Rotation, grooveRotationInterpolationRate));
                }
            }
        }

        //下半身の処理を行う
        public void Complement(int frameNumber)
        {
            //次に下半身の処理を行う、おそらく下半身に位置情報はないが一応位置情報の更新も行う
            BoneNames lowerBodyBoneName = BoneNames.下半身;
            if (hips == null) { return; }
            VMD.BoneKeyFrame lowerBodyVMDBoneFrame = VMDReader.GetBoneKeyFrame(lowerBodyBoneName, frameNumber);

            if (boneGhost.GhostDictionary.Keys.Contains(BoneNames.上半身)
            && boneGhost.GhostDictionary[BoneNames.上半身].enabled
            && boneGhost.GhostDictionary[BoneNames.上半身].ghost != null
            && boneGhost.GhostDictionary.Keys.Contains(BoneNames.センター)
            && boneGhost.GhostDictionary[BoneNames.センター].enabled
            && boneGhost.GhostDictionary[BoneNames.センター].ghost != null)
            {
                if (lowerBodyVMDBoneFrame != null)
                {
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition += lowerBodyVMDBoneFrame.Position * DefaultBoneAmplifier;
                    boneGhost.GhostDictionary[BoneNames.上半身].ghost.localPosition -= lowerBodyVMDBoneFrame.Position * DefaultBoneAmplifier;
                    boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation.PlusRotation(lowerBodyVMDBoneFrame.Rotation);
                    boneGhost.GhostDictionary[BoneNames.上半身].ghost.localRotation = boneGhost.GhostDictionary[BoneNames.上半身].ghost.localRotation.MinusRotation(lowerBodyVMDBoneFrame.Rotation);
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
                        boneGhost.GhostDictionary[BoneNames.センター].ghost.localPosition += deltaVector;
                        boneGhost.GhostDictionary[BoneNames.上半身].ghost.localPosition -= deltaVector;
                    }

                    if (nextRotationVMDBoneFrame != null && lastRotationVMDBoneFrame != null)
                    {
                        float rotationInterpolationRate = lowerBodyVMDBoneGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation, frameNumber, lastRotationVMDBoneFrame.FrameNumber, nextRotationVMDBoneFrame.FrameNumber);

                        Quaternion deltaQuaternion = Quaternion.Lerp(lastRotationVMDBoneFrame.Rotation, nextRotationVMDBoneFrame.Rotation, rotationInterpolationRate);
                        boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation = boneGhost.GhostDictionary[BoneNames.センター].ghost.localRotation.PlusRotation(deltaQuaternion);
                        boneGhost.GhostDictionary[BoneNames.上半身].ghost.localRotation = boneGhost.GhostDictionary[BoneNames.上半身].ghost.localRotation.MinusRotation(deltaQuaternion);
                    }
                }
            }
        }
    }

    //VMDでは足IKはFootの差分のみの位置、回転情報を持つ
    //また、このコードで足先IKは未実装である
    class FootIK
    {
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

        Dictionary<Transform, Quaternion> boneOriginalRotationDictionary;
        Dictionary<Transform, Quaternion> boneOriginalLocalRotationDictionary;
         
        public Transform Target { get; private set; }
        private BoneNames footBoneName;
        private Vector3 firstLocalPosition;

        private Vector3 firstHipDown;
        private Vector3 firstHipRight;

        private float upperLegLength = 0;
        private float lowerLegLength = 0;
        private float legLength = 0;
        private float targetDistance = 0;

        public FootIK(VMDReader vmdReader, Animator animator, Feet foot, Vector3 offset)
        {
            VMDReader = vmdReader;
            Foot = foot;
            Animator = animator;
            firstHipDown = -animator.transform.up;
            firstHipRight = animator.transform.right;
            //注意！オフセットのy座標を逆にしている
            Offset = new Vector3(offset.x, -offset.y, offset.z);

            if (Foot == Feet.LeftFoot)
            {
                footBoneName = BoneNames.左足ＩＫ;
                HipTransform = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                KneeTransform = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                FootTransform = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            }
            else
            {
                footBoneName = BoneNames.右足ＩＫ;
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

            boneOriginalLocalRotationDictionary = new Dictionary<Transform, Quaternion>()
            {
                { HipTransform, HipTransform.localRotation },
                { KneeTransform, KneeTransform.localRotation },
                { FootTransform, FootTransform.localRotation }
            };

            boneOriginalRotationDictionary = new Dictionary<Transform, Quaternion>()
            {
                { HipTransform, HipTransform.rotation },
                { KneeTransform, KneeTransform.rotation },
                { FootTransform, FootTransform.rotation }
            };

            GameObject targetGameObject = new GameObject();
            targetGameObject.transform.position = FootTransform.position;
            targetGameObject.transform.parent = Animator.transform;
            Target = targetGameObject.transform;
            firstLocalPosition = Target.localPosition;
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

            HipTransform.localRotation = boneOriginalLocalRotationDictionary[HipTransform];
            Vector3 hipDown = HipTransform.rotation * Quaternion.Inverse(boneOriginalRotationDictionary[HipTransform]) * firstHipDown;
            HipTransform.RotateAround(HipTransform.position, Vector3.Cross(hipDown, targetVector), Vector3.Angle(hipDown, targetVector));
            Vector3 hipRight = HipTransform.rotation * Quaternion.Inverse(boneOriginalRotationDictionary[HipTransform]) * firstHipRight;
            HipTransform.RotateAround(HipTransform.position, hipRight, -hipAngle);
            hipRight = HipTransform.rotation * Quaternion.Inverse(boneOriginalRotationDictionary[HipTransform]) * firstHipRight;
            KneeTransform.localRotation = boneOriginalLocalRotationDictionary[KneeTransform];
            KneeTransform.RotateAround(KneeTransform.position, hipRight, kneeAngle);
        }
        public bool IK(int frameNumber)
        {
            SetIKEnable(frameNumber);

            if (!Enable) { return false; }

            VMD.BoneKeyFrame footIKFrame = VMDReader.GetBoneKeyFrame(footBoneName, frameNumber);

            if (footIKFrame == null || footIKFrame.Position == Vector3.zero) { return false; }

            FootTransform.localRotation = boneOriginalLocalRotationDictionary[FootTransform];

            Vector3 moveVector = footIKFrame.Position ;

            Target.localPosition = firstLocalPosition + (moveVector * DefaultBoneAmplifier) + Offset;

            IK();

            #region 足首
            //if (footIKFrame.Rotation == ZeroQuaternion) { return true; }
            //FootTransform.localRotation = footIKFrame.Rotation;

            ////つま先IK
            //VMD.BoneKeyFrame toeIKFrame = VMDReader.GetBoneKeyFrame(toeBoneName, frameNumber);
            //if (toeIKFrame == null) { return true; }
            //if (toeIKFrame.Rotation == ZeroQuaternion) { return true; }
            //Vector3 toeVector = ToeTransform.position - FootTransform.position;
            //Vector3 toeTargetVector = toeVector + toeIKFrame.Position * DefaultBoneAmplifier;
            //FootTransform.localRotation = FootTransform.localRotation * Quaternion.FromToRotation(toeVector, toeTargetVector);
            #endregion 足首

            return true;
        }
        public void InterpolateIK(int frameNumber)
        {
            SetIKEnable(frameNumber);

            if (!Enable) { return; }

            FootTransform.localRotation = boneOriginalLocalRotationDictionary[FootTransform];

            VMDReader.BoneKeyFrameGroup vmdFootBoneFrameGroup = VMDReader.GetBoneKeyFrameGroup(footBoneName);
            VMD.BoneKeyFrame lastPositionFootVMDBoneFrame = vmdFootBoneFrameGroup.LastPositionKeyFrame;
            VMD.BoneKeyFrame nextPositionFootVMDBoneFrame = vmdFootBoneFrameGroup.NextPositionKeyFrame;

            if (nextPositionFootVMDBoneFrame != null && lastPositionFootVMDBoneFrame != null)
            {
                float xInterpolationRate = vmdFootBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, frameNumber, lastPositionFootVMDBoneFrame.FrameNumber, nextPositionFootVMDBoneFrame.FrameNumber);
                float yInterpolationRate = vmdFootBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Y, frameNumber, lastPositionFootVMDBoneFrame.FrameNumber, nextPositionFootVMDBoneFrame.FrameNumber);
                float zInterpolationRate = vmdFootBoneFrameGroup.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Z, frameNumber, lastPositionFootVMDBoneFrame.FrameNumber, nextPositionFootVMDBoneFrame.FrameNumber);

                float xInterpolation = Mathf.Lerp(lastPositionFootVMDBoneFrame.Position.x, nextPositionFootVMDBoneFrame.Position.x, xInterpolationRate);
                float yInterpolation = Mathf.Lerp(lastPositionFootVMDBoneFrame.Position.y, nextPositionFootVMDBoneFrame.Position.y, yInterpolationRate);
                float zInterpolation = Mathf.Lerp(lastPositionFootVMDBoneFrame.Position.z, nextPositionFootVMDBoneFrame.Position.z, zInterpolationRate);

                Vector3 moveVector = new Vector3(xInterpolation, yInterpolation, zInterpolation);
                Target.localPosition = firstLocalPosition + (moveVector * DefaultBoneAmplifier) + Offset;
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
            //    Vector3 toeTargetVector = toeVector + new Vector3(xInterpolation, yInterpolation, zInterpolation) * DefaultBoneAmplifier;
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

    //裏で正規化されたモデル
    //(初期ポーズで各ボーンのlocalRotationがQuaternion.identityのモデル)を疑似的にアニメーションさせる
    class BoneGhost
    {
        public Dictionary<BoneNames, (Transform ghost, bool enabled)> GhostDictionary { get; private set; } = new Dictionary<BoneNames, (Transform ghost, bool enabled)>();
        public Dictionary<BoneNames, Vector3> OriginalGhostLocalPositionDictionary { get; private set; } = new Dictionary<BoneNames, Vector3>();
        public Dictionary<BoneNames, Quaternion> OriginalRotationDictionary { get; private set; } = new Dictionary<BoneNames, Quaternion>();
        public Dictionary<BoneNames, Quaternion> OriginalGhostRotationDictionary { get; private set; } = new Dictionary<BoneNames, Quaternion>();

        private Dictionary<BoneNames, Transform> boneDictionary = new Dictionary<BoneNames, Transform>();

        const string GhostSalt = "Ghost";

        public bool Enabled = true;

        public BoneGhost(Animator animator, Dictionary<BoneNames, Transform> boneDictionary)
        {
            this.boneDictionary = boneDictionary;

            Dictionary<BoneNames, (BoneNames optionParent1, BoneNames optionParent2, BoneNames necessaryParent)> boneParentDictionary
                = new Dictionary<BoneNames, (BoneNames optionParent1, BoneNames optionParent2, BoneNames necessaryParent)>()
            {
                { BoneNames.センター, (BoneNames.None, BoneNames.None, BoneNames.全ての親) },
                { BoneNames.左足,     (BoneNames.None, BoneNames.None, BoneNames.センター) },
                { BoneNames.左ひざ,   (BoneNames.None, BoneNames.None, BoneNames.左足) },
                { BoneNames.左足首,   (BoneNames.None, BoneNames.None, BoneNames.左ひざ) },
                { BoneNames.右足,     (BoneNames.None, BoneNames.None, BoneNames.センター) },
                { BoneNames.右ひざ,   (BoneNames.None, BoneNames.None, BoneNames.右足) },
                { BoneNames.右足首,   (BoneNames.None, BoneNames.None, BoneNames.右ひざ) },
                { BoneNames.上半身,   (BoneNames.None, BoneNames.None, BoneNames.センター) },
                { BoneNames.上半身2,  (BoneNames.None, BoneNames.None, BoneNames.上半身) },
                { BoneNames.首,       (BoneNames.上半身2, BoneNames.None, BoneNames.上半身) },
                { BoneNames.頭,       (BoneNames.首, BoneNames.上半身2, BoneNames.上半身) },
                { BoneNames.左肩,     (BoneNames.上半身2, BoneNames.None, BoneNames.上半身) },
                { BoneNames.左腕,     (BoneNames.左肩, BoneNames.上半身2, BoneNames.上半身) },
                { BoneNames.左ひじ,   (BoneNames.None, BoneNames.None, BoneNames.左腕) },
                { BoneNames.左手首,   (BoneNames.None, BoneNames.None, BoneNames.左ひじ) },
                { BoneNames.左親指１, (BoneNames.左手首, BoneNames.None, BoneNames.None) },
                { BoneNames.左親指２, (BoneNames.左親指１, BoneNames.None, BoneNames.None) },
                { BoneNames.左人指１, (BoneNames.左手首, BoneNames.None, BoneNames.None) },
                { BoneNames.左人指２, (BoneNames.左人指１, BoneNames.None, BoneNames.None) },
                { BoneNames.左人指３, (BoneNames.左人指２, BoneNames.None, BoneNames.None) },
                { BoneNames.左中指１, (BoneNames.左手首, BoneNames.None, BoneNames.None) },
                { BoneNames.左中指２, (BoneNames.左中指１, BoneNames.None, BoneNames.None) },
                { BoneNames.左中指３, (BoneNames.左中指２, BoneNames.None, BoneNames.None) },
                { BoneNames.左薬指１, (BoneNames.左手首, BoneNames.None, BoneNames.None) },
                { BoneNames.左薬指２, (BoneNames.左薬指１, BoneNames.None, BoneNames.None) },
                { BoneNames.左薬指３, (BoneNames.左薬指２, BoneNames.None, BoneNames.None) },
                { BoneNames.左小指１, (BoneNames.左手首, BoneNames.None, BoneNames.None) },
                { BoneNames.左小指２, (BoneNames.左小指１, BoneNames.None, BoneNames.None) },
                { BoneNames.左小指３, (BoneNames.左小指２, BoneNames.None, BoneNames.None) },
                { BoneNames.右肩,     (BoneNames.上半身2, BoneNames.None, BoneNames.上半身) },
                { BoneNames.右腕,     (BoneNames.右肩, BoneNames.上半身2, BoneNames.上半身) },
                { BoneNames.右ひじ,   (BoneNames.None, BoneNames.None, BoneNames.右腕) },
                { BoneNames.右手首,   (BoneNames.None, BoneNames.None, BoneNames.右ひじ) },
                { BoneNames.右親指１, (BoneNames.右手首, BoneNames.None, BoneNames.None) },
                { BoneNames.右親指２, (BoneNames.右親指１, BoneNames.None, BoneNames.None) },
                { BoneNames.右人指１, (BoneNames.右手首, BoneNames.None, BoneNames.None) },
                { BoneNames.右人指２, (BoneNames.右人指１, BoneNames.None, BoneNames.None) },
                { BoneNames.右人指３, (BoneNames.右人指２, BoneNames.None, BoneNames.None) },
                { BoneNames.右中指１, (BoneNames.右手首, BoneNames.None, BoneNames.None) },
                { BoneNames.右中指２, (BoneNames.右中指１, BoneNames.None, BoneNames.None) },
                { BoneNames.右中指３, (BoneNames.右中指２, BoneNames.None, BoneNames.None) },
                { BoneNames.右薬指１, (BoneNames.右手首, BoneNames.None, BoneNames.None) },
                { BoneNames.右薬指２, (BoneNames.右薬指１, BoneNames.None, BoneNames.None) },
                { BoneNames.右薬指３, (BoneNames.右薬指２, BoneNames.None, BoneNames.None) },
                { BoneNames.右小指１, (BoneNames.右手首, BoneNames.None, BoneNames.None) },
                { BoneNames.右小指２, (BoneNames.右小指１, BoneNames.None, BoneNames.None) },
                { BoneNames.右小指３, (BoneNames.右小指２, BoneNames.None, BoneNames.None) },
            };

            //Ghostの生成
            foreach (BoneNames boneName in boneDictionary.Keys)
            {
                if (boneName == BoneNames.全ての親 || boneName == BoneNames.左足ＩＫ || boneName == BoneNames.右足ＩＫ)
                {
                    continue;
                }

                if (boneDictionary[boneName] == null)
                {
                    GhostDictionary.Add(boneName, (null, false));
                    continue;
                }

                Transform ghost = new GameObject(boneDictionary[boneName].name + GhostSalt).transform;
                ghost.position = boneDictionary[boneName].position;
                ghost.rotation = animator.transform.rotation;
                GhostDictionary.Add(boneName, (ghost, true));
            }

            //Ghostの親子構造を設定
            foreach (BoneNames boneName in boneDictionary.Keys)
            {
                if (boneName == BoneNames.全ての親 || boneName == BoneNames.左足ＩＫ || boneName == BoneNames.右足ＩＫ)
                {
                    continue;
                }

                if (GhostDictionary[boneName].ghost == null || !GhostDictionary[boneName].enabled)
                {
                    continue;
                }

                if (boneName == BoneNames.センター)
                {
                    GhostDictionary[boneName].ghost.SetParent(animator.transform);
                    continue;
                }

                if (boneParentDictionary[boneName].optionParent1 != BoneNames.None && boneDictionary[boneParentDictionary[boneName].optionParent1] != null)
                {
                    GhostDictionary[boneName].ghost.SetParent(GhostDictionary[boneParentDictionary[boneName].optionParent1].ghost);
                }
                else if (boneParentDictionary[boneName].optionParent2 != BoneNames.None && boneDictionary[boneParentDictionary[boneName].optionParent2] != null)
                {
                    GhostDictionary[boneName].ghost.SetParent(GhostDictionary[boneParentDictionary[boneName].optionParent2].ghost);
                }
                else if (boneParentDictionary[boneName].necessaryParent != BoneNames.None && boneDictionary[boneParentDictionary[boneName].necessaryParent] != null)
                {
                    GhostDictionary[boneName].ghost.SetParent(GhostDictionary[boneParentDictionary[boneName].necessaryParent].ghost);
                }
                else
                {
                    GhostDictionary[boneName] = (GhostDictionary[boneName].ghost, false);
                }
            }

            foreach (BoneNames boneName in GhostDictionary.Keys)
            {
                if (GhostDictionary[boneName].ghost == null || !GhostDictionary[boneName].enabled)
                {
                    OriginalGhostLocalPositionDictionary.Add(boneName, Vector3.zero);
                    OriginalGhostRotationDictionary.Add(boneName, Quaternion.identity);
                    OriginalRotationDictionary.Add(boneName, Quaternion.identity);
                }
                else
                {
                    OriginalGhostLocalPositionDictionary.Add(boneName, GhostDictionary[boneName].ghost.localPosition);
                    OriginalGhostRotationDictionary.Add(boneName, GhostDictionary[boneName].ghost.rotation);
                    OriginalRotationDictionary.Add(boneName, boneDictionary[boneName].rotation);
                }
            }

            //足についてはIKがやってくれるはず
            SetLeftFootGhostEnable(false);
            SetRightFootGhostEnable(true);
        }

        public void GhostAll()
        {
            foreach (BoneNames boneName in GhostDictionary.Keys)
            {
                if (GhostDictionary[boneName].ghost == null || !GhostDictionary[boneName].enabled) { return; }

                //Ghostを動かした後、実体を動かす
                boneDictionary[boneName].position = GhostDictionary[boneName].ghost.position;

                boneDictionary[boneName].rotation
                    = GhostDictionary[boneName].ghost.rotation
                    * Quaternion.Inverse(OriginalGhostRotationDictionary[boneName])
                    * OriginalRotationDictionary[boneName];
            }
        }

        public void SetLeftFootGhostEnable(bool enabled)
        {
            if (GhostDictionary.Keys.Contains(BoneNames.左足))
                GhostDictionary[BoneNames.左足] = (GhostDictionary[BoneNames.左足].ghost, enabled);
            if (GhostDictionary.Keys.Contains(BoneNames.左ひざ))
                GhostDictionary[BoneNames.左ひざ] = (GhostDictionary[BoneNames.左ひざ].ghost, enabled);
            if (GhostDictionary.Keys.Contains(BoneNames.左足首))
                GhostDictionary[BoneNames.左足首] = (GhostDictionary[BoneNames.左足首].ghost, enabled);
        }
        public void SetRightFootGhostEnable(bool enabled)
        {
            if (GhostDictionary.Keys.Contains(BoneNames.右足))
                GhostDictionary[BoneNames.右足] = (GhostDictionary[BoneNames.右足].ghost, enabled);
            if (GhostDictionary.Keys.Contains(BoneNames.右ひざ))
                GhostDictionary[BoneNames.右ひざ] = (GhostDictionary[BoneNames.右ひざ].ghost, enabled);
            if (GhostDictionary.Keys.Contains(BoneNames.右足首))
                GhostDictionary[BoneNames.右足首] = (GhostDictionary[BoneNames.右足首].ghost, enabled);
        }
    }
}