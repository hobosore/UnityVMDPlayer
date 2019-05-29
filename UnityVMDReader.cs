//利用上の注意
//よろしく使ってください。

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;

namespace UnityVMDReader
{
    //注意！VMDファイルではShitJISが用いられているが、UnityでShiftJISを使うには一工夫必要！！
    //Unity ShiftJISで検索すること

    class VMDReader
    {
        //人ボーンのキーフレームのボーンごとの集合
        public class BoneKeyFrameGroup
        {
            readonly Quaternion ZeroQuaternion = new Quaternion(0, 0, 0, 0);

            public enum BoneNames
            {
                全ての親, センター, グルーブ, 左足ＩＫ, 左つま先ＩＫ, 右足ＩＫ, 右つま先ＩＫ, 下半身, 上半身, 上半身2,
                首, 頭, 左目, 右目, 両目, 左肩, 左腕, 左腕捩れ, 左ひじ, 左手首,
                右肩, 右腕, 右腕捩れ, 右ひじ, 右手首, 左足, 左ひざ, 左足首, 左つま先, 右足,
                右ひざ, 右足首, 右つま先, 左親指１, 左親指２, 左人指１, 左人指２, 左人指３, 左中指１, 左中指２,
                左中指３, 左薬指１, 左薬指２, 左薬指３, 左小指１, 左小指２, 左小指３, 右親指１, 右親指２, 右人指１,
                右人指２, 右人指３, 右中指１, 右中指２, 右中指３, 右薬指１, 右薬指２, 右薬指３, 右小指１, 右小指２,
                右小指３
            }

            public static List<string> BoneStringNames
            {
                get
                {
                    if (boneStringNames == null)
                    {
                        boneStringNames = Enum.GetNames(typeof(BoneNames)).ToList();
                    }

                    return boneStringNames;
                }

                set { }
            }
            private static List<string> boneStringNames;

            public BoneNames Name { get; private set; }

            public List<VMD.BoneKeyFrame> KeyFrames = new List<VMD.BoneKeyFrame>();

            public VMD.BoneKeyFrame CurrentFrame { get; private set; }
            public VMD.BoneKeyFrame.Interpolation Interpolation { get; private set; }
            public VMD.BoneKeyFrame LastKeyFrame { get; private set; }
            public VMD.BoneKeyFrame LastPositionKeyFrame { get; private set; }
            public VMD.BoneKeyFrame LastRotationKeyFrame { get; private set; }
            public VMD.BoneKeyFrame NextPositionKeyFrame { get; private set; }
            public VMD.BoneKeyFrame NextRotationKeyFrame { get; private set; }

            public BoneKeyFrameGroup(BoneNames name)
            {
                Name = name;
            }

            public VMD.BoneKeyFrame GetKeyFrame(int frame)
            {
                CurrentFrame = KeyFrames.Find(x => x.Frame == frame);
                if (CurrentFrame == null) { return CurrentFrame; }

                LastKeyFrame = CurrentFrame;

                if (CurrentFrame.Position != Vector3.zero)
                {
                    LastPositionKeyFrame = CurrentFrame;
                }

                if (CurrentFrame.Rotation != ZeroQuaternion)
                {
                    LastRotationKeyFrame = CurrentFrame;
                }

                NextPositionKeyFrame = KeyFrames.Find(x => x.Frame > frame && x.Position != Vector3.zero);
                NextRotationKeyFrame = KeyFrames.Find(x => x.Frame > frame && x.Rotation != ZeroQuaternion);

                Interpolation = CurrentFrame.BoneInterpolation;

                return CurrentFrame;
            }

            public VMD.BoneKeyFrame GetKeyFrameAsJump(int frame)
            {
                CurrentFrame = KeyFrames.Find(x => x.Frame == frame);

                //ToListゆるして
                KeyFrames = KeyFrames.OrderByDescending(x => x.Frame).ToList();
                LastPositionKeyFrame = KeyFrames.Find(x => x.Frame < frame && x.Position != Vector3.zero);
                LastRotationKeyFrame = KeyFrames.Find(x => x.Frame < frame && x.Rotation != ZeroQuaternion);

                LastKeyFrame = LastPositionKeyFrame.Frame < LastRotationKeyFrame.Frame ? LastRotationKeyFrame : LastPositionKeyFrame;

                //ToListゆるして
                KeyFrames = KeyFrames.OrderBy(x => x.Frame).ToList();
                NextPositionKeyFrame = KeyFrames.Find(x => x.Frame > frame && x.Position != Vector3.zero);
                NextRotationKeyFrame = KeyFrames.Find(x => x.Frame > frame && x.Rotation != ZeroQuaternion);

                if (LastKeyFrame != null)
                {
                    Interpolation = LastKeyFrame.BoneInterpolation;
                }

                return CurrentFrame;
            }

            public void AddFrame(VMD.BoneKeyFrame vmdBoneFrame)
            {
                KeyFrames.Add(vmdBoneFrame);
            }

            public void OrderByFrame()
            {
                KeyFrames = KeyFrames.OrderBy(x => x.Frame).ToList();
            }
        }

        public VMD RawVMD { get; private set; }

        public int FrameCount { get; private set; } = -1;

        //ボーンごとに分けたキーフレームの集合をボーンの番号順にリストに入れる、これはコンストラクタで初期化される
        public List<BoneKeyFrameGroup> BoneKeyFrameGroups = new List<BoneKeyFrameGroup>();

        void InitializeBoneKeyFrameGroups()
        {
            BoneKeyFrameGroups.Clear();
            for (int i = 0; i < BoneKeyFrameGroup.BoneStringNames.Count; i++)
            {
                BoneKeyFrameGroups.Add(new BoneKeyFrameGroup((BoneKeyFrameGroup.BoneNames)i));
            }
        }
        public VMDReader()
        {
            InitializeBoneKeyFrameGroups();
            RawVMD = new VMD();
        }
        public VMDReader(string filePath)
        {
            InitializeBoneKeyFrameGroups();
            ReadVMD(filePath);
        }

        public void ReadVMD(string filePath)
        {
            RawVMD = new VMD(filePath);

            //人ボーンのキーフレームをグループごとに分けてBoneKeyFrameGroupsに入れる
            InitializeBoneKeyFrameGroups();
            foreach (VMD.BoneKeyFrame boneKeyFrame in RawVMD.BoneKeyFrames)
            {
                if (!BoneKeyFrameGroup.BoneStringNames.Contains(boneKeyFrame.Name)) { continue; }
                BoneKeyFrameGroups[BoneKeyFrameGroup.BoneStringNames.IndexOf(boneKeyFrame.Name)].AddFrame(boneKeyFrame);
            }

            //いちおうフレームごとに並べておく
            BoneKeyFrameGroups.ForEach(x => x.OrderByFrame());

            //ついでに最終フレームも求めておく
            FrameCount = BoneKeyFrameGroups.Where(x => x.KeyFrames.Count > 0).Max(x => x.KeyFrames.Last().Frame);
        }
    }

    class VMD
    {
        //人ボーンのキーフレーム
        public class BoneKeyFrame
        {
            //ボーン名
            public string Name { get; private set; } = "";

            //フレーム番号
            public int Frame { get; private set; }

            //ボーンの位置、UnityでいうlocalPosition、ただし縮尺はUnityの空間の約10倍
            public Vector3 Position { get; private set; }

            //ボーンの回転、UnityでいうlocalRotation
            public Quaternion Rotation { get; private set; }

            //3次ベジェ曲線での補間
            public class Interpolation
            {
                public class BezierCurvePoint
                {
                    internal byte X = new byte();
                    internal byte Y = new byte();
                }

                internal BezierCurvePoint[] X = new BezierCurvePoint[] { new BezierCurvePoint(), new BezierCurvePoint() };
                internal BezierCurvePoint[] Y = new BezierCurvePoint[] { new BezierCurvePoint(), new BezierCurvePoint() };
                internal BezierCurvePoint[] Z = new BezierCurvePoint[] { new BezierCurvePoint(), new BezierCurvePoint() };
                internal BezierCurvePoint[] Rotation = new BezierCurvePoint[] { new BezierCurvePoint(), new BezierCurvePoint() };

                //コンストラクタにてX,Y,Z,Rotationを入れる
                List<BezierCurvePoint[]> BezierCurves;
                public enum BezierCurveNames { X, Y, Z, Rotation }

                public Interpolation()
                {
                    BezierCurves = new List<BezierCurvePoint[]>() { X, Y, Z, Rotation };
                }

                //P0,P1,P2,P3を通る3次のもので、
                //P0 = (0,0)と P3 = (1,1)かつ、0 < x < 1で単調増加となるようなベジェ曲線が用いられている。
                //P1とP2がVMDファイルから得られるので理論上曲線が求まるが、下では媒介変数表示と2分法を用いて値を近似している。
                //edvakfさんのコードを参考にしました。
                public float GetInterpolationValue(BezierCurveNames bazierCurveName, int currentFrame, int beginFrame, int endFrame)
                {
                    BezierCurvePoint[] bezierCurve = BezierCurves[(int)bazierCurveName];

                    float x = (float)(currentFrame - beginFrame) / (float)(endFrame - beginFrame);

                    float t = 0.5f;
                    float s = 0.5f;
                    for (int i = 0; i < 15; i++)
                    {
                        //実は保存されているときには127*127である。それを比に落とし込む。
                        float zero = (3 * s * s * t * bezierCurve[0].X / 127) + (3 * s * t * t * bezierCurve[1].X / 127) + (t * t * t) - x;

                        if (Mathf.Abs(zero) < 0.00001f) { break; }

                        if (zero > 0) { t -= 1 / (4 * Mathf.Pow(2, i)); }
                        else { t += 1 / (4 * Mathf.Pow(2, i)); }

                        s = 1 - t;
                    }

                    //実は保存されているときには127*127である。それを比に落とし込む。
                    return (3 * s * s * t * bezierCurve[0].Y / 127) + (3 * s * t * t * bezierCurve[1].Y / 127) + (t * t * t);
                }
            }

            public Interpolation BoneInterpolation = new Interpolation();

            public BoneKeyFrame() { }
            public BoneKeyFrame(Stream stream) { Read(stream); }

            public void Read(Stream stream)
            {
                BinaryReader binaryReader = new BinaryReader(stream);
                byte[] nameBytes = binaryReader.ReadBytes(15);
                Name = System.Text.Encoding.GetEncoding("shift_jis").GetString(nameBytes);
                //ヌル文字除去
                Name = Name.TrimEnd('\0').TrimEnd('?').TrimEnd('\0');
                Frame = binaryReader.ReadInt32();
                float[] positionArray = (from n in Enumerable.Range(0, 3) select binaryReader.ReadSingle()).ToArray();
                //座標系の違いにより、xをマイナスにすることに注意
                Position = new Vector3(-positionArray[0], positionArray[1], positionArray[2]);
                float[] rotationArray = (from n in Enumerable.Range(0, 4) select binaryReader.ReadSingle()).ToArray();
                //座標系の違いにより、x,zをマイナスにすることに注意
                Rotation = new Quaternion(-rotationArray[0], rotationArray[1], -rotationArray[2], rotationArray[3]);

                //VMDでは3次ベジェ曲線において
                //X軸の補間 パラメータP1(X_x1, X_y1),P2(X_x2, X_y2)
                //Y軸の補間 パラメータP1(Y_x1, Y_y1),P2(Y_x2, Y_y2)
                //Z軸の補間 パラメータP1(Z_x1, Z_y1),P2(Z_x2, Z_y2)
                //回転の補間パラメータP1(R_x1, R_y1),P2(R_x2, R_y2)
                //としたとき、インデックスでいうと0番目から4,8番目と4の倍数のところに
                //X_x1, X_y1, X_x2, X_y2, Y_x1, Y_y1, ...と順番に入っている
                //また、X_x1などの値はすべて1byteである

                void parseInterpolation(Interpolation.BezierCurvePoint[] x)
                {
                    x[0].X = binaryReader.ReadByte();
                    binaryReader.ReadBytes(3);
                    x[0].Y = binaryReader.ReadByte();
                    binaryReader.ReadBytes(3);
                    x[1].X = binaryReader.ReadByte();
                    binaryReader.ReadBytes(3);
                    x[1].Y = binaryReader.ReadByte();
                    binaryReader.ReadBytes(3);
                }

                parseInterpolation(BoneInterpolation.X);
                parseInterpolation(BoneInterpolation.Y);
                parseInterpolation(BoneInterpolation.Z);
                parseInterpolation(BoneInterpolation.Rotation);
            }
        };

        //表情のキーフレーム
        public class FaceKeyFrame
        {
            //表情モーフ名
            public string MorphName { get; private set; }
            //表情モーフのウェイト
            public float Weight { get; private set; }
            //フレーム番号
            public uint Frame { get; private set; }

            public FaceKeyFrame() { }
            public FaceKeyFrame(Stream stream) { Read(stream); }

            public void Read(Stream stream)
            {
                BinaryReader binaryReader = new BinaryReader(stream);
                byte[] nameBytes = binaryReader.ReadBytes(15);
                MorphName = System.Text.Encoding.GetEncoding("shift_jis").GetString(nameBytes);
                //ヌル文字除去
                MorphName = MorphName.TrimEnd('\0').TrimEnd('?').TrimEnd('\0');
                Frame = binaryReader.ReadUInt32();
                Weight = binaryReader.ReadSingle();
            }
        };

        //カメラのキーフレーム
        public class CameraKeyFrame
        {
            //フレーム番号
            public int Frame { get; private set; }
            //目標点とカメラの距離(目標点がカメラ前面でマイナス)
            public float Distance { get; private set; }
            //目標点の位置
            public Vector3 Position { get; private set; }
            //カメラの回転
            public Quaternion Rotation { get; private set; }
            //補間曲線
            public Interpolation CameraInterpolation = new Interpolation();
            //public byte[][] InterPolation { get; private set; } = new byte[6][];
            //視野角
            public float Angle;
            //おそらくパースペクティブかどうか0or1、0でパースペクティブ
            public bool Perspective;

            //3次ベジェ曲線での補間
            public class Interpolation
            {
                public class BezierCurvePoint
                {
                    internal byte X = new byte();
                    internal byte Y = new byte();
                }

                internal BezierCurvePoint[] X = new BezierCurvePoint[] { new BezierCurvePoint(), new BezierCurvePoint() };
                internal BezierCurvePoint[] Y = new BezierCurvePoint[] { new BezierCurvePoint(), new BezierCurvePoint() };
                internal BezierCurvePoint[] Z = new BezierCurvePoint[] { new BezierCurvePoint(), new BezierCurvePoint() };
                internal BezierCurvePoint[] Rotation = new BezierCurvePoint[] { new BezierCurvePoint(), new BezierCurvePoint() };
                internal BezierCurvePoint[] Distance = new BezierCurvePoint[] { new BezierCurvePoint(), new BezierCurvePoint() };
                internal BezierCurvePoint[] Angle = new BezierCurvePoint[] { new BezierCurvePoint(), new BezierCurvePoint() };

                //コンストラクタにてX,Y,Z,Rotationを入れる
                List<BezierCurvePoint[]> BezierCurves;
                public enum BezierCurveNames { X, Y, Z, Rotation, Distance, Angle }

                public Interpolation()
                {
                    BezierCurves = new List<BezierCurvePoint[]>() { X, Y, Z, Rotation, Distance, Angle };
                }

                //P0,P1,P2,P3を通る3次のもので、
                //P0 = (0,0)と P3 = (1,1)かつ、0 < x < 1で単調増加となるようなベジェ曲線が用いられている。
                //P1とP2がVMDファイルから得られるので理論上曲線が求まるが、下では媒介変数表示と2分法を用いて値を近似している。
                //edvakfさんのコードを参考にしました。
                public float GetInterpolationValue(BezierCurveNames bazierCurveName, int currentFrame, int beginFrame, int endFrame)
                {
                    BezierCurvePoint[] bezierCurve = BezierCurves[(int)bazierCurveName];

                    float x = (float)(currentFrame - beginFrame) / (float)(endFrame - beginFrame);

                    float t = 0.5f;
                    float s = 0.5f;
                    for (int i = 0; i < 15; i++)
                    {
                        //実は保存されているときには127*127である。それを比に落とし込む。
                        float zero = (3 * s * s * t * bezierCurve[0].X / 127) + (3 * s * t * t * bezierCurve[1].X / 127) + (t * t * t) - x;

                        if (Mathf.Abs(zero) < 0.00001f) { break; }

                        if (zero > 0) { t -= 1 / (4 * Mathf.Pow(2, i)); }
                        else { t += 1 / (4 * Mathf.Pow(2, i)); }

                        s = 1 - t;
                    }

                    //実は保存されているときには127*127である。それを比に落とし込む。
                    return (3 * s * s * t * bezierCurve[0].Y / 127) + (3 * s * t * t * bezierCurve[1].Y / 127) + (t * t * t);
                }
            }

            public CameraKeyFrame() { }
            public CameraKeyFrame(Stream stream) { Read(stream); }

            public void Read(Stream stream)
            {
                BinaryReader binaryReader = new BinaryReader(stream);
                Frame = binaryReader.ReadInt32();

                //目標点とカメラの距離(目標点がカメラ前面でマイナス)
                Distance = binaryReader.ReadInt32();
                float[] positionArray = (from n in Enumerable.Range(0, 3) select binaryReader.ReadSingle()).ToArray();

                //座標系、xをマイナスにすることに注意
                Position = new Vector3(-positionArray[0], positionArray[1], positionArray[2]);
                float[] rotationArray = (from n in Enumerable.Range(0, 4) select binaryReader.ReadSingle()).ToArray();

                //座標系、x,zをマイナスにすることに注意
                Rotation = new Quaternion(-rotationArray[0], rotationArray[1], -rotationArray[2], rotationArray[3]);

                void parseInterpolation(Interpolation.BezierCurvePoint[] x)
                {
                    x[0].X = binaryReader.ReadByte();
                    x[0].Y = binaryReader.ReadByte();
                    x[1].X = binaryReader.ReadByte();
                    x[1].Y = binaryReader.ReadByte();
                }

                parseInterpolation(CameraInterpolation.X);
                parseInterpolation(CameraInterpolation.Y);
                parseInterpolation(CameraInterpolation.Z);
                parseInterpolation(CameraInterpolation.Rotation);
                parseInterpolation(CameraInterpolation.Distance);
                parseInterpolation(CameraInterpolation.Angle);

                Angle = binaryReader.ReadSingle();
                Perspective = BitConverter.ToBoolean(binaryReader.ReadBytes(3), 0);
            }
        };

        //照明のキーフレーム
        public class LightKeyFrame
        {
            //フレーム番号
            public int Frame { get; private set; }
            //ライトの色、R,G,Bの順に格納されている、0から1
            public float[] LightColor { get; private set; } = new float[3];
            //ライトの位置
            public float[] Position { get; private set; } = new float[3];

            public LightKeyFrame() { }
            public LightKeyFrame(Stream stream) { Read(stream); }

            public void Read(Stream stream)
            {
                BinaryReader binaryReader = new BinaryReader(stream);
                Frame = binaryReader.ReadInt32();

                //R,G,Bの順に格納されている、0から1
                float[] LightColor = (from n in Enumerable.Range(0, 3) select binaryReader.ReadSingle()).ToArray();

                float[] positionArray = (from n in Enumerable.Range(0, 3) select binaryReader.ReadSingle()).ToArray();
                //座標系の違いによりxをマイナスとする
                Position = new float[] { -positionArray[0], positionArray[1], positionArray[2] };
            }
        };

        //セルフ影のキーフレーム
        public class SelfShadowKeyFrame
        {
            public int Frame { get; private set; }
            //セルフシャドウの種類
            public byte Type { get; private set; }
            //セルフシャドウの距離
            public float Distance { get; private set; }

            public SelfShadowKeyFrame() { }
            public SelfShadowKeyFrame(Stream stream) { Read(stream); }

            public void Read(Stream stream)
            {
                BinaryReader binaryReader = new BinaryReader(stream);
                Frame = binaryReader.ReadInt32();
                Type = binaryReader.ReadByte();
                Distance = binaryReader.ReadSingle();
            }
        }

        //IKのキーフレーム
        public class IKKeyFrame
        {
            //IKの名前とそのIKが有効かどうか
            public class VMDIKEnable
            {
                public string IKName;
                public bool Enable;
            };

            public int Frame { get; private set; }
            //
            public bool Display { get; private set; }
            public List<VMDIKEnable> IKEnable { get; private set; } = new List<VMDIKEnable>();

            public IKKeyFrame() { }
            public IKKeyFrame(Stream stream) { Read(stream); }

            public void Read(Stream stream)
            {
                BinaryReader binaryReader = new BinaryReader(stream);
                byte[] buffer = new byte[20];
                Frame = binaryReader.ReadInt32();
                Display = BitConverter.ToBoolean(new byte[] { binaryReader.ReadByte() }, 0);
                int ikCount = binaryReader.ReadInt32();
                for (int i = 0; i < ikCount; i++)
                {
                    binaryReader.Read(buffer, 0, 20);
                    VMDIKEnable vmdIKEnable = new VMDIKEnable
                    {
                        //Shift_JISでヌル文字除去
                        IKName = System.Text.Encoding.GetEncoding("shift_jis").GetString(buffer).TrimEnd('\0').TrimEnd('?').TrimEnd('\0'),
                        Enable = BitConverter.ToBoolean(new byte[] { binaryReader.ReadByte() }, 0)
                    };
                    IKEnable.Add(vmdIKEnable);
                }
            }
        };

        public string MotionName = "None";
        public float Version = -1;
        //最終フレーム
        public int FrameCount = -1;

        //人ボーンのキーフレームのリスト
        public List<BoneKeyFrame> BoneKeyFrames = new List<BoneKeyFrame>();
        //表情モーフのキーフレームのリスト
        public List<FaceKeyFrame> FaceFrames = new List<FaceKeyFrame>();
        //カメラのキーフレームのリスト
        public List<CameraKeyFrame> CameraFrames = new List<CameraKeyFrame>();
        //照明のキーフレームのリスト
        public List<LightKeyFrame> LightFrames = new List<LightKeyFrame>();
        //セルフ影のキーフレームのリスト
        public List<SelfShadowKeyFrame> SelfShadowKeyFrames = new List<SelfShadowKeyFrame>();
        //IKのキーフレームのリスト
        public List<IKKeyFrame> IKFrames = new List<IKKeyFrame>();

        public VMD() { }
        public VMD(string filePath) { LoadFromFile(filePath); }

        public void LoadFromStream(Stream stream)
        {
            try
            {
                BinaryReader binaryReader = new BinaryReader(stream);
                char[] buffer = new char[30];

                //ファイルタイプの読み込み
                string RightFileType = "Vocaloid Motion Data";
                byte[] fileTypeBytes = binaryReader.ReadBytes(30);
                string fileType = System.Text.Encoding.GetEncoding("shift_jis").GetString(fileTypeBytes).Substring(0, RightFileType.Length);
                if (!fileType.Equals("Vocaloid Motion Data"))
                {
                    Debug.Log("読み込もうとしたファイルはVMDファイルではありません");
                }

                //バージョンの読み込み、バージョンは後で使用していない
                Version = BitConverter.ToSingle((from c in buffer select Convert.ToByte(c)).ToArray(), 0);

                //モーション名の読み込み、Shift_JISで保存されている
                byte[] nameBytes = binaryReader.ReadBytes(20);
                MotionName = System.Text.Encoding.GetEncoding("shift_jis").GetString(nameBytes);
                //ヌル文字除去
                MotionName = MotionName.TrimEnd('\0').TrimEnd('?').TrimEnd('\0');

                //人ボーンのキーフレームの読み込み
                int boneFrameCount = binaryReader.ReadInt32();
                for (int i = 0; i < boneFrameCount; i++)
                {
                    BoneKeyFrames.Add(new BoneKeyFrame(stream));
                }

                //表情モーフのキーフレームの読み込み
                int faceFrameCount = binaryReader.ReadInt32();
                for (int i = 0; i < faceFrameCount; i++)
                {
                    FaceFrames.Add(new FaceKeyFrame(stream));
                }
                FaceFrames = FaceFrames.OrderBy(x => x.Frame).ToList();

                //カメラのキーフレームの読み込み
                int cameraFrameCount = binaryReader.ReadInt32();
                for (int i = 0; i < cameraFrameCount; i++)
                {
                    CameraFrames.Add(new CameraKeyFrame(stream));
                }
                CameraFrames = CameraFrames.OrderBy(x => x.Frame).ToList();

                //照明のキーフレームの読み込み
                int lightFrameCount = binaryReader.ReadInt32();
                for (int i = 0; i < lightFrameCount; i++)
                {
                    LightFrames.Add(new LightKeyFrame(stream));
                }
                LightFrames = LightFrames.OrderBy(x => x.Frame).ToList();

                //vmdのバージョンによってはここで終わる
                if (binaryReader.BaseStream.Position == binaryReader.BaseStream.Length) { return; }

                //セルフシャドウの読み込み
                int selfShadowFrameCount = binaryReader.ReadInt32();
                for (int i = 0; i < selfShadowFrameCount; i++)
                {
                    SelfShadowKeyFrames.Add(new SelfShadowKeyFrame(stream));
                }
                SelfShadowKeyFrames = SelfShadowKeyFrames.OrderBy(x => x.Frame).ToList();

                //vmdのバージョンによってはここで終わる
                if (binaryReader.BaseStream.Position == binaryReader.BaseStream.Length) { return; }

                //IKのキーフレームの読み込み
                int ikFrameCount = binaryReader.ReadInt32();
                for (int i = 0; i < ikFrameCount; i++)
                {
                    IKFrames.Add(new IKKeyFrame(stream));
                }

                //ここで終わってないとおかしい
                if (binaryReader.BaseStream.Position != binaryReader.BaseStream.Length)
                {
                    Debug.Log("データの最後に不明な部分があります");
                }

                return;
            }
            catch
            {
                Debug.Log("VMD読み込みエラー");
                return;
            }
        }
        public void LoadFromFile(string filePath)
        {
            using (FileStream fileStream = File.OpenRead(filePath))
            {
                LoadFromStream(fileStream);
            }
        }
    };
}