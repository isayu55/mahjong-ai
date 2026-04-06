using UnityEngine;
using Unity.InferenceEngine; // 💡 Sentisを使用する場合はこちら
using System.Collections.Generic;
using System.Linq;

public class YoloTileDetector : MonoBehaviour
{
    [Header("YOLOモデル (best.onnx)")]
    public ModelAsset yoloModelAsset;

    [Header("連携先スクリプト")]
    public TextUIManager textUIManager;

    private Worker worker;
    private const int IMAGE_SIZE = 640;
    
    // 💡 今回学習させた全30クラス（Roboflowのアルファベット順に基づく）
    private const int NUM_CLASSES = 30; 
    private const int NUM_ANCHORS = 8400;
    private const float CONF_THRESHOLD = 0.25f;
    private const float IOU_THRESHOLD = 0.45f;

    // 💡 【超重要】Roboflowのアルファベット順(0~29) を GameManagerの順番(0~35) に変換する辞書
    private int[] yoloToGameManagerId = new int[]
    {
        33, // 0: chun -> 中(33)
        27, // 1: east -> 東(27)
        31, // 2: haku -> 白(31)
        32, // 3: hatsu -> 發(32)
        0,  // 4: man1 -> 一萬(0)
        8,  // 5: man9 -> 九萬(8)
        30, // 6: north -> 北(30)
        9, 10, 11, 12, 13, // 7-11: pin1~5 -> 一筒(9)〜五筒(13)
        34, // 12: pin5r -> 赤五筒(34) ★追加
        14, 15, 16, 17,    // 13-16: pin6~9 -> 六筒(14)〜九筒(17)
        18, 19, 20, 21, 22,// 17-21: sou1~5 -> 一索(18)〜五索(22)
        35, // 22: sou5r -> 赤五索(35) ★追加
        23, 24, 25, 26,    // 23-26: sou6~9 -> 六索(23)〜九索(26)
        28, // 27: south -> 南(28)
        -1, // 28: tile -> 無視する（-1を設定） ★追加
        29  // 29: west -> 西(29)
    };

    void Start()
    {
        // if (yoloModelAsset != null)
        // {
        //     Model model = ModelLoader.Load(yoloModelAsset);
        //     worker = new Worker(model, BackendType.GPUPixel);
        //     Debug.Log("👁️ YOLOv8 視覚野モデルのロード完了！");
        // }
    }

    // 💡 モデルが必要になった瞬間にだけ初期化するメソッド
    private void InitializeModelIfNeeded()
    {
        if (worker == null && yoloModelAsset != null)
        {
            Debug.Log("🔄 YOLOv8 モデルをメモリに読み込み中...");
            Model model = ModelLoader.Load(yoloModelAsset);
            
            // 💡 修正：「WASM」ではなく「CPU」と指定します
            worker = new Worker(model, BackendType.CPU); 
            Debug.Log("👁️ YOLOv8 ロード完了！");
        }
    }

    void OnDestroy()
    {
        worker?.Dispose();
    }

    public void DetectTiles(Texture2D inputTexture)
    {

        InitializeModelIfNeeded();

        if (worker == null) return;

        Texture2D resizedTex = ResizeTexture(inputTexture, IMAGE_SIZE, IMAGE_SIZE);
        
        // 💡 修正：先に空箱(1, 3, 幅, 高さ)を作ってから、画像を変換して流し込む最新の書き方
        using Tensor<float> inputTensor = new Tensor<float>(new TensorShape(1, 3, IMAGE_SIZE, IMAGE_SIZE));
        TextureConverter.ToTensor(resizedTex, inputTensor, new TextureTransform());
        
        worker.Schedule(inputTensor);
        
        using Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
        float[] outputArray = outputTensor.DownloadToArray();

        List<Detection> detections = new List<Detection>();

        for (int i = 0; i < NUM_ANCHORS; i++)
        {
            float maxConf = 0;
            int bestClassId = -1;

            for (int c = 0; c < NUM_CLASSES; c++)
            {
                float conf = outputArray[(4 + c) * NUM_ANCHORS + i];
                if (conf > maxConf)
                {
                    maxConf = conf;
                    bestClassId = c;
                }
            }

            if (maxConf > CONF_THRESHOLD)
            {
                float xc = outputArray[0 * NUM_ANCHORS + i];
                float yc = outputArray[1 * NUM_ANCHORS + i];
                float w  = outputArray[2 * NUM_ANCHORS + i];
                float h  = outputArray[3 * NUM_ANCHORS + i];

                detections.Add(new Detection {
                    classId = bestClassId, confidence = maxConf,
                    xMin = xc - (w / 2f), yMin = yc - (h / 2f),
                    xMax = xc + (w / 2f), yMax = yc + (h / 2f),
                    centerX = xc
                });
            }
        }

        List<Detection> finalDetections = ApplyNMS(detections);
        finalDetections = finalDetections.OrderBy(d => d.centerX).ToList();

        List<TileType> detectedTiles = new List<TileType>();
        foreach (var d in finalDetections)
        {
            if (d.classId >= 0 && d.classId < yoloToGameManagerId.Length)
            {
                int gameManagerId = yoloToGameManagerId[d.classId];
                
                // 💡 "tile"クラス（-1）以外ならリストに追加する
                if (gameManagerId != -1) 
                {
                    detectedTiles.Add((TileType)gameManagerId);
                }
            }
        }

        if (textUIManager != null)
        {
            textUIManager.SetHandFromImage(detectedTiles);
        }

        Debug.Log($"📸 解析完了！ {detectedTiles.Count} 枚の牌を検出しました。");
        Destroy(resizedTex);
    }

    private Texture2D ResizeTexture(Texture2D source, int newWidth, int newHeight)
    {
        RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
        RenderTexture.active = rt;
        Graphics.Blit(source, rt);
        Texture2D nTex = new Texture2D(newWidth, newHeight, TextureFormat.RGB24, false);
        nTex.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
        nTex.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return nTex;
    }

    private List<Detection> ApplyNMS(List<Detection> detections)
    {
        var result = new List<Detection>();
        detections = detections.OrderByDescending(d => d.confidence).ToList();

        while (detections.Count > 0)
        {
            var best = detections[0];
            result.Add(best);
            detections.RemoveAt(0);
            detections.RemoveAll(d => d.classId == best.classId && CalculateIoU(best, d) > IOU_THRESHOLD);
        }
        return result;
    }

    private float CalculateIoU(Detection box1, Detection box2)
    {
        float x1 = Mathf.Max(box1.xMin, box2.xMin);
        float y1 = Mathf.Max(box1.yMin, box2.yMin);
        float x2 = Mathf.Min(box1.xMax, box2.xMax);
        float y2 = Mathf.Min(box1.yMax, box2.yMax);
        float intersection = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
        float area1 = (box1.xMax - box1.xMin) * (box1.yMax - box1.yMin);
        float area2 = (box2.xMax - box2.xMin) * (box2.yMax - box2.yMin);
        return intersection / (area1 + area2 - intersection);
    }

    class Detection
    {
        public int classId; public float confidence;
        public float xMin, yMin, xMax, yMax, centerX;
    }
}