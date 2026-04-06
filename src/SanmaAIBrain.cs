using System;
using UnityEngine;
using System.Collections.Generic;
using Unity.InferenceEngine; 
using System.Linq;

public class SanmaAIBrain : MonoBehaviour
{
    [Header("AIモデル設定 (Dual Brain)")]
    public ModelAsset brainAAsset; // ここに新しい v5 の ONNX をセットします
    public ModelAsset brainBAsset; 
    
    private Worker workerA;
    private Worker workerB;

    // 👇ここを追加：リーチアクションの出力にゲタを履かせるバイアス
    [Header("アクション重み調整 (Logits Bias)")]
    [Range(0f, 10.0f)]
    public float riichiBias = 2.0f; // プラスにするほどリーチしやすくなる
    [Range(0f, 10.0f)]
    public float kanBias = 0.0f;    // カンも同様に調整可能にしておくと便利です

    void Start()
    {
        if (brainAAsset != null) {
            workerA = new Worker(ModelLoader.Load(brainAAsset), BackendType.GPUPixel);
            Debug.Log("🧠 Brain A (v5: 22ch/137out) 起動完了！");
        }
        if (brainBAsset != null) {
            workerB = new Worker(ModelLoader.Load(brainBAsset), BackendType.GPUPixel);
            Debug.Log("🧠 Brain B (鳴きAI) 起動完了！");
        }
    }

    // ==========================================
    // 🚀 真・三麻AI v5: 打牌・リーチ・カン選択 (22チャンネル)
    // ==========================================
    public int DecideAction_v5(
        int myIndex, 
        List<TileType> myHand, 
        List<TileType>[] allRivers, 
        List<GameManager.MeldData>[] allMelds, 
        bool[] allRiichi, 
        List<TileType> doraIndicators,
        int currentKyoku,
        int currentOyaIndex,
        float remainingWallRatio,
        int[] playerScores)
    {
        if (workerA == null) return (int)myHand[myHand.Count - 1]; 

        float[] inputData = new float[22 * 34];

        // 💡 相対インデックス（0:自分, 1:下家, 2:対面）
        int shimo = (myIndex + 1) % 3;
        int toi = (myIndex + 2) % 3;

        // 手牌のカウント（マスク処理用）
        int[] handCount = new int[34];
        foreach (var t in myHand) {
            int id = GetSafeId(t);
            inputData[0 * 34 + id] += 1.0f; // ch[0]: 自分の手牌
            handCount[id]++;
        }

        // ch[1]-[3]: 河（自分、下家、対面）
        foreach (var t in allRivers[myIndex]) inputData[1 * 34 + GetSafeId(t)] = 1.0f;
        foreach (var t in allRivers[shimo]) inputData[2 * 34 + GetSafeId(t)] = 1.0f;
        foreach (var t in allRivers[toi]) inputData[3 * 34 + GetSafeId(t)] = 1.0f;

        // ch[4]-[6]: 鳴き（自分、下家、対面）
        Action<int, int> applyMelds = (pIndex, chOffset) => {
            foreach (var m in allMelds[pIndex]) {
                float c = (m.type == GameManager.MeldType.Kita) ? 1f : ((m.type == GameManager.MeldType.Pon) ? 3f : 4f);
                inputData[chOffset * 34 + GetSafeId(m.tile)] += c;
            }
        };
        applyMelds(myIndex, 4); applyMelds(shimo, 5); applyMelds(toi, 6);

        // ch[7]: ドラ表示牌・赤ドラ
        inputData[7 * 34 + 13] = 1.0f; inputData[7 * 34 + 22] = 1.0f; 
        foreach(var d in doraIndicators) inputData[7 * 34 + GetSafeId(d)] = 1.0f;

        // ch[8]: ヤオチュウ牌
        int[] yaochu = { 0, 8, 27, 28, 29, 30, 31, 32, 33 };
        foreach (int y in yaochu) inputData[8 * 34 + y] = 1.0f;

        // ch[9]-[10]: 相手のリーチ (全体に1.0を立てる)
        if (allRiichi[shimo]) for (int i = 0; i < 34; i++) inputData[9 * 34 + i] = 1.0f;
        if (allRiichi[toi]) for (int i = 0; i < 34; i++) inputData[10 * 34 + i] = 1.0f;

        // ch[11]-[13]: 親が誰か
        int oyaCh = (currentOyaIndex == myIndex) ? 11 : (currentOyaIndex == shimo ? 12 : 13);
        for (int i = 0; i < 34; i++) inputData[oyaCh * 34 + i] = 1.0f;

        // ch[14]: 局 (0=東1, 1=東2...)
        inputData[14 * 34 + (currentKyoku - 1)] = 1.0f;

        // ch[15]: 山の残り枚数の割合
        for (int i = 0; i < 34; i++) inputData[15 * 34 + i] = remainingWallRatio;

        // ch[16]-[18]: 点数状況（10万点で1.0に正規化）
        for (int i = 0; i < 34; i++) {
            inputData[16 * 34 + i] = playerScores[myIndex] / 100000f;
            inputData[17 * 34 + i] = playerScores[shimo] / 100000f;
            inputData[18 * 34 + i] = playerScores[toi] / 100000f;
        }

        // ch[19]: 自分がリーチ中か
        if (allRiichi[myIndex]) for (int i = 0; i < 34; i++) inputData[19 * 34 + i] = 1.0f;

        // ch[20]-[21]: 相手の現物（安全牌）
        foreach (var t in allRivers[shimo]) inputData[20 * 34 + GetSafeId(t)] = 1.0f;
        foreach (var t in allRivers[toi]) inputData[21 * 34 + GetSafeId(t)] = 1.0f;

        // --- 推論実行 ---
        // 🚨 22チャンネルのテンソルを作成
        using var inputTensor = new Tensor<float>(new TensorShape(1, 22, 1, 34), inputData);
        workerA.SetInput("input", inputTensor);
        workerA.Schedule();

        using var outputTensor = workerA.PeekOutput("output") as Tensor<float>;
        float[] result = outputTensor.DownloadToArray(); // 長さ137の配列になる！

        

        // 🚨 ここにあった無条件の forループ (result[i] += riichiBias;) は削除します！

        // --- アクションのフィルタリング（持っていない牌のカンなどを防止） ---
        bool isMenzen = !allMelds[myIndex].Any(m => m.type == GameManager.MeldType.Pon || m.type == GameManager.MeldType.Daiminkan || m.type == GameManager.MeldType.Shouminkan);
        
        int bestAction = -1;
        float bestScore = -float.MaxValue;

        for (int i = 0; i < 137; i++)
        {
            bool isValid = false;
            
            if (i < 34) {
                isValid = handCount[i] > 0;
            } else if (i < 68) {
                int discardId = i - 34;
                if (handCount[discardId] > 0 && isMenzen && !allRiichi[myIndex]) {
                    // 💡 修正：テンパイ判定を行い、テンパイ時のみリーチを許可＆勇気(インスペクターの値)を足す！
                    List<TileType> tempHand = new List<TileType>(myHand);
                    int removeIdx = tempHand.FindIndex(t => GetSafeId(t) == discardId);
                    if (removeIdx != -1) {
                        tempHand.RemoveAt(removeIdx);
                        if (IsTenpai13(tempHand)) {
                            isValid = true;
                            result[i] += riichiBias; // 🦸‍♂️ コンポーネントで設定した値をここで足す
                        }
                    }
                }
            } else if (i < 102) {
                if (handCount[i - 68] == 4 && !allRiichi[myIndex]) {
                    isValid = true;
                    result[i] += kanBias; // カンも同様にここで足す
                }
            } else if (i == 136) {
                // 北抜き (136)
                isValid = handCount[30] > 0;
            }

            if (isValid && result[i] > bestScore) {
                bestScore = result[i];
                bestAction = i;
            }
        }

        // もし有効なアクションが選べなかった場合のフェイルセーフ（ツモ切り）
        if (bestAction == -1) bestAction = GetSafeId(myHand[myHand.Count - 1]);

        return bestAction;
    }

    // ==========================================
    // 💡 鳴き選択 AI (Brain B: 変更なし)
    // ==========================================
    public int DecideNaki(
        int myIndex, List<TileType> myHand, List<TileType>[] allRivers, List<GameManager.MeldData>[] allMelds, 
        bool[] allRiichi, List<TileType> doraIndicators, int[] visibleTilesCount, TileType discardedTile, int discarderIndex)
    {
        if (workerB == null) return 0; 
        float[] inputData = new float[10 * 34];
        foreach (var t in myHand) inputData[0 * 34 + GetSafeId(t)] += 1.0f;
        foreach (var t in allRivers[myIndex]) inputData[1 * 34 + GetSafeId(t)] = 1.0f;
        for (int p = 0; p < 3; p++) {
            if (p == myIndex) continue;
            foreach (var t in allRivers[p]) inputData[2 * 34 + GetSafeId(t)] = 1.0f;
        }
        inputData[4 * 34 + 13] = 1.0f; inputData[4 * 34 + 22] = 1.0f; 
        if (doraIndicators.Count > 0) inputData[4 * 34 + GetSafeId(doraIndicators[0])] = 1.0f;
        int[] yaochu = { 0, 8, 27, 28, 29, 30, 31, 32, 33 };
        foreach (int y in yaochu) inputData[5 * 34 + y] = 1.0f;
        foreach (var m in allMelds[myIndex]) inputData[6 * 34 + GetSafeId(m.tile)] += (m.type == GameManager.MeldType.Kita) ? 1f : ((m.type == GameManager.MeldType.Pon) ? 3f : 4f);
        for (int p = 0; p < 3; p++) {
            if (p == myIndex) continue;
            foreach (var m in allMelds[p]) inputData[7 * 34 + GetSafeId(m.tile)] += (m.type == GameManager.MeldType.Kita) ? 1f : ((m.type == GameManager.MeldType.Pon) ? 3f : 4f);
        }
        inputData[9 * 34 + GetSafeId(discardedTile)] = 1.0f;

        using var inputTensor = new Tensor<float>(new TensorShape(1, 10, 1, 34), inputData);
        workerB.SetInput("input", inputTensor);
        workerB.Schedule();
        using var outputTensor = workerB.PeekOutput("output") as Tensor<float>;
        float[] result = outputTensor.DownloadToArray();

        // Tensor<float> outputTensor = workerA.PeekOutput("output") as Tensor<float>;
        // float[] probabilities = outputTensor.DownloadToArray();

        int bestAction = 0; float maxProb = -float.MaxValue;
        for (int i = 0; i < 3; i++) if (result[i] > maxProb) { maxProb = result[i]; bestAction = i; }
        return bestAction;
    }

    private int GetSafeId(TileType t) {
        int id = (int)t;
        if (id == 34) return 13; if (id == 35) return 22;
        return id;
    }

    // 💡 AIの推論結果（全137次元の生データ）をそのまま取得するメソッドを追加
    // ==========================================
    // 💡 AIの推論結果（全137次元の生データ）を取得するメソッド
    // ==========================================
    public float[] GetActionProbabilities_v5(
        int myIndex, List<TileType> myHand, List<TileType>[] allRivers, List<GameManager.MeldData>[] allMelds, 
        bool[] allRiichi, List<TileType> doraIndicators, int currentKyoku, int currentOyaIndex, float remainingWallRatio, int[] playerScores)
    {
        // 🚨 ワーカーが未セットの場合は安全のため0埋め配列を返す
        if (workerA == null) return new float[137];

        float[] inputData = new float[22 * 34];
        int shimo = (myIndex + 1) % 3;
        int toi = (myIndex + 2) % 3;

        // ch[0]: 手牌
        foreach (var t in myHand) inputData[0 * 34 + GetSafeId(t)] += 1.0f;

        // ch[1]-[3]: 河
        foreach (var t in allRivers[myIndex]) inputData[1 * 34 + GetSafeId(t)] = 1.0f;
        foreach (var t in allRivers[shimo]) inputData[2 * 34 + GetSafeId(t)] = 1.0f;
        foreach (var t in allRivers[toi]) inputData[3 * 34 + GetSafeId(t)] = 1.0f;

        // ch[4]-[6]: 鳴き
        Action<int, int> applyMelds = (pIndex, chOffset) => {
            foreach (var m in allMelds[pIndex]) {
                float c = (m.type == GameManager.MeldType.Kita) ? 1f : ((m.type == GameManager.MeldType.Pon) ? 3f : 4f);
                inputData[chOffset * 34 + GetSafeId(m.tile)] += c;
            }
        };
        applyMelds(myIndex, 4); applyMelds(shimo, 5); applyMelds(toi, 6);

        // ch[7]: ドラ表示牌・赤ドラ
        inputData[7 * 34 + 13] = 1.0f; inputData[7 * 34 + 22] = 1.0f; 
        foreach(var d in doraIndicators) inputData[7 * 34 + GetSafeId(d)] = 1.0f;

        // ch[8]: ヤオチュウ牌
        int[] yaochu = { 0, 8, 27, 28, 29, 30, 31, 32, 33 };
        foreach (int y in yaochu) inputData[8 * 34 + y] = 1.0f;

        // ch[9]-[10]: 相手のリーチ
        if (allRiichi[shimo]) for (int i = 0; i < 34; i++) inputData[9 * 34 + i] = 1.0f;
        if (allRiichi[toi]) for (int i = 0; i < 34; i++) inputData[10 * 34 + i] = 1.0f;

        // ch[11]-[13]: 親が誰か
        int oyaCh = (currentOyaIndex == myIndex) ? 11 : (currentOyaIndex == shimo ? 12 : 13);
        for (int i = 0; i < 34; i++) inputData[oyaCh * 34 + i] = 1.0f;

        // ch[14]: 局 (0=東1, 1=東2...)
        inputData[14 * 34 + (currentKyoku - 1)] = 1.0f;

        // ch[15]: 山の残り枚数の割合
        for (int i = 0; i < 34; i++) inputData[15 * 34 + i] = remainingWallRatio;

        // ch[16]-[18]: 点数状況（10万点で1.0に正規化）
        for (int i = 0; i < 34; i++) {
            inputData[16 * 34 + i] = playerScores[myIndex] / 100000f;
            inputData[17 * 34 + i] = playerScores[shimo] / 100000f;
            inputData[18 * 34 + i] = playerScores[toi] / 100000f;
        }

        // ch[19]: 自分がリーチ中か
        if (allRiichi[myIndex]) for (int i = 0; i < 34; i++) inputData[19 * 34 + i] = 1.0f;

        // ch[20]-[21]: 相手の現物（安全牌）
        foreach (var t in allRivers[shimo]) inputData[20 * 34 + GetSafeId(t)] = 1.0f;
        foreach (var t in allRivers[toi]) inputData[21 * 34 + GetSafeId(t)] = 1.0f;

        // --- 本物のAI推論実行！ ---
        using var inputTensor = new Tensor<float>(new TensorShape(1, 22, 1, 34), inputData);
        workerA.SetInput("input", inputTensor);
        workerA.Schedule();

        using var outputTensor = workerA.PeekOutput("output") as Tensor<float>;
        
        // 💡 修正：ここでゲタ(riichiBias)を履かせず、AIの「純粋な生データ」をそのまま返す！
        // ゲタを履かせる処理は、呼び出し元の GameManager 側で行われます。
        return outputTensor.DownloadToArray(); 
    }
    // ==========================================
    // 💡 AIBrain内蔵 テンパイ判定ロジック
    // ==========================================
    bool IsTenpai13(List<TileType> hand13) {
        for (int i = 0; i < 34; i++) {
            int[] counts = new int[34];
            foreach (var t in hand13) counts[GetSafeId(t)]++;
            counts[i]++; 
            if (IsChiitoitsu(counts) || IsKokushiMusou(counts) || CheckNormalAgari(counts, 4)) return true; 
        }
        return false;
    }
    bool CheckNormalAgari(int[] counts, int requiredMentsu) {
        for (int i = 0; i < 34; i++) {
            if (counts[i] >= 2) {
                counts[i] -= 2;
                if (CheckMentsu(counts, 0, requiredMentsu)) { counts[i] += 2; return true; }
                counts[i] += 2;
            }
        }
        return false;
    }
    bool CheckMentsu(int[] counts, int startIndex, int requiredMentsu) {
        if (requiredMentsu == 0) return true;
        int i = startIndex; while (i < 34 && counts[i] == 0) i++;
        if (i >= 34) return false;
        if (counts[i] >= 3) {
            counts[i] -= 3;
            if (CheckMentsu(counts, i, requiredMentsu - 1)) { counts[i] += 3; return true; }
            counts[i] += 3;
        }
        if (i < 27 && (i % 9) < 7) {
            if (counts[i] > 0 && counts[i + 1] > 0 && counts[i + 2] > 0) {
                counts[i]--; counts[i + 1]--; counts[i + 2]--;
                if (CheckMentsu(counts, i, requiredMentsu - 1)) { counts[i]++; counts[i + 1]++; counts[i + 2]++; return true; }
                counts[i]++; counts[i + 1]++; counts[i + 2]++;
            }
        }
        return false;
    }
    bool IsChiitoitsu(int[] counts) {
        int pairCount = 0;
        for (int i = 0; i < 34; i++) {
            if (counts[i] == 2) pairCount++; else if (counts[i] > 0) return false;
        }
        return pairCount == 7;
    }
    bool IsKokushiMusou(int[] counts) {
        int[] yaochu = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
        bool hasPair = false;
        foreach (int i in yaochu) {
            if (counts[i] == 0) return false;
            if (counts[i] == 2) hasPair = true;
            if (counts[i] > 2) return false;
        }
        return hasPair;
    }

    void OnDestroy() { workerA?.Dispose(); workerB?.Dispose(); }
}