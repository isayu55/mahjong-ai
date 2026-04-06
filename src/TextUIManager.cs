using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro; 
using System.Linq;
using System;
using UnityEngine.Networking;
using System.Text;

public class TextUIManager : MonoBehaviour
{
    [Header("AIブレインの参照")]
    public SanmaAIBrain aiBrain;
    
    [Header("UI設定")]
    public List<Sprite> tileSprites; 
    public GameObject tileButtonPrefab;
    public Transform selectionGrid; 
    public Transform handPanel;     
    
    [Header("推論結果表示用")]
    public TextMeshProUGUI resultText;       
    public Image recommendedTileImage;
    public GameObject resultBackgroundPanel; 

    [Header("AI解説表示用")]
    public GameObject explanationScrollView;
    public TextMeshProUGUI explanationText;

    [Header("UIサイズ設定 (スマホ対応)")]
    public Vector2 selectionTileSize = new Vector2(75, 100); 
    public Vector2 handTileSize = new Vector2(60, 85);       
    public Vector2 riverTileSize = new Vector2(40, 55);      
    public Vector2 meldTileSize = new Vector2(45, 60);       

    [Header("AI確率調整（Temperature）")]
    [Range(1.0f, 5.0f)]
    public float softmaxTemperature = 2.0f;

    public List<TileType> currentHand = new List<TileType>();
    
    private readonly string[] tileJapaneseNames = {
        "一萬", "二萬", "三萬", "四萬", "五萬", "六萬", "七萬", "八萬", "九萬",
        "一筒", "二筒", "三筒", "四筒", "五筒", "六筒", "七筒", "八筒", "九筒",
        "一索", "二索", "三索", "四索", "五索", "六索", "七索", "八索", "九索",
        "東", "南", "西", "北", "白", "發", "中"
    };

    class DiscardInfo
    {
        public int baseTileId;
        public float normalProb; 
        public float riichiProb; 
        public float totalProb;  
        public bool leadsToTenpai; 
    }

    void Start()
    {
        if (recommendedTileImage != null) recommendedTileImage.enabled = false;
        if (resultBackgroundPanel != null) resultBackgroundPanel.SetActive(false); 
        if (resultText != null) resultText.text = "14枚選択してください...";
        if (explanationScrollView != null) explanationScrollView.SetActive(false);
        for (int i = 0; i < 36; i++) 
        {
            if (i >= tileSprites.Count || tileSprites[i] == null) continue; 

            GameObject btnObj = Instantiate(tileButtonPrefab, selectionGrid);
            
            RectTransform rt = btnObj.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = selectionTileSize;

            TileButton script = btnObj.GetComponent<TileButton>();
            script.Setup((TileType)i, tileSprites[i], AddToHand);

            if (i >= 1 && i <= 7)
            {
                Button btn = btnObj.GetComponent<Button>();
                Image img = btnObj.GetComponent<Image>();
                
                if (btn != null) btn.interactable = false; 
                if (img != null) img.color = new Color(0.3f, 0.3f, 0.3f, 1f); 
            }
        }
    }

    void AddToHand(TileType type)
    {
        if (currentHand.Count >= 14) return;

        currentHand.Add(type);
        SortHand(currentHand); 
        RefreshHandUI();

        if (currentHand.Count == 14)
        {
            AnalyzeHandWithAI();
        }
    }

    public void SortHand(List<TileType> hand)
    {
        hand.Sort((a, b) => {
            float valA = ((int)a == 34) ? 13.5f : (((int)a == 35) ? 22.5f : (int)a);
            float valB = ((int)b == 34) ? 13.5f : (((int)b == 35) ? 22.5f : (int)b);
            return valA.CompareTo(valB);
        });
    }

    void RefreshHandUI()
    {
        foreach (Transform child in handPanel) Destroy(child.gameObject);

        for (int i = 0; i < currentHand.Count; i++)
        {
            TileType tile = currentHand[i];

            if (i == currentHand.Count - 1 && currentHand.Count == 14) {
                GameObject spacer = new GameObject("Spacer", typeof(RectTransform));
                spacer.transform.SetParent(handPanel, false);
                spacer.GetComponent<RectTransform>().sizeDelta = new Vector2(10, 47);
            }

            // 💡 ここを変更！Buttonコンポーネントを追加してクリック可能にする
            GameObject newTile = new GameObject("HandTile", typeof(Image), typeof(Button));
            newTile.transform.SetParent(handPanel, false);
            
            Image img = newTile.GetComponent<Image>();
            img.sprite = tileSprites[(int)tile];
            newTile.GetComponent<RectTransform>().sizeDelta = handTileSize;

            // 💡 クリック時の削除イベントを登録
            Button btn = newTile.GetComponent<Button>();
            int index = i; // ラムダ式用に変数にコピー
            btn.onClick.AddListener(() => OnTileClicked(index));
        }
    }

    // 💡 新規追加：手牌がクリックされたときの処理
    private void OnTileClicked(int index)
    {
        if (index >= 0 && index < currentHand.Count)
        {
            Debug.Log($"📸 牌を削除しました: {currentHand[index]}");
            currentHand.RemoveAt(index);
            
            SortHand(currentHand);
            RefreshHandUI();
            
            // 推論結果をリセットして再入力を促す
            resultText.text = $"牌を削除しました。\n現在 {currentHand.Count} 枚です。足りない牌を追加してください。";
            if (recommendedTileImage != null) recommendedTileImage.enabled = false;

            if (resultBackgroundPanel != null) resultBackgroundPanel.SetActive(false); 
            if (explanationScrollView != null) explanationScrollView.SetActive(false);
        }
    }

    public void ClearHand()
    {
        currentHand.Clear();
        foreach (Transform child in handPanel) Destroy(child.gameObject);
        if (resultText != null) resultText.text = " 14枚選択してください...";
        if (recommendedTileImage != null) recommendedTileImage.enabled = false;
        if (resultBackgroundPanel != null) resultBackgroundPanel.SetActive(false); 
        if (explanationText != null) explanationText.text = "";
        if (explanationScrollView != null) explanationScrollView.SetActive(false);
    }

    // ==========================================
    // 💡 AIの推論と詳細な確率表示
    // ==========================================
    void AnalyzeHandWithAI()
    {

        string promptHandStr = ConvertHandToPromptString(currentHand);
        Debug.Log("🤖 LLMに渡す手牌文字列: " + promptHandStr);
        
        resultText.text = "AI分析中...";

        List<TileType>[] dummyRivers = new List<TileType>[3] { new List<TileType>(), new List<TileType>(), new List<TileType>() };
        List<GameManager.MeldData>[] dummyMelds = new List<GameManager.MeldData>[3] { new List<GameManager.MeldData>(), new List<GameManager.MeldData>(), new List<GameManager.MeldData>() };
        bool[] dummyRiichi = new bool[3];
        List<TileType> dummyDora = new List<TileType> { (TileType)0 }; 
        int[] scores = new int[] { 35000, 35000, 35000 };

        float[] rawLogits = aiBrain.GetActionProbabilities_v5(0, currentHand, dummyRivers, dummyMelds, dummyRiichi, dummyDora, 1, 0, 1.0f, scores);

        bool isAllZero = true;
        foreach (float f in rawLogits) { if (f != 0f) { isAllZero = false; break; } }
        
        if (isAllZero) {
            resultText.text = "<color=#FF5555>⚠️ 警告：AIの出力がすべて0です。</color>\n\nSanmaAIBrain に ONNXモデル がセットされていないか、推論が失敗しています。";
            return;
        }

        var uniqueBaseIds = currentHand.Select(t => {
            int id = (int)t;
            if (id == 34) return 13; 
            if (id == 35) return 22; 
            return id;
        }).Distinct().ToList();

        var validActions = new List<KeyValuePair<int, float>>();
        Dictionary<int, bool> canRiichiDict = new Dictionary<int, bool>();
        bool globalCanRiichi = false;

        foreach (int baseId in uniqueBaseIds)
        {
            List<TileType> tempHand = new List<TileType>(currentHand);
            int removeIndex = tempHand.FindIndex(t => {
                int id = (int)t;
                return id == baseId || (id == 34 && baseId == 13) || (id == 35 && baseId == 22);
            });
            if (removeIndex != -1) tempHand.RemoveAt(removeIndex);

            bool tenpai = IsTenpai13(tempHand);
            canRiichiDict[baseId] = tenpai;
            if (tenpai) globalCanRiichi = true;

            validActions.Add(new KeyValuePair<int, float>(baseId, rawLogits[baseId])); 
            
            if (tenpai) {
                float riichiScore = rawLogits[baseId + 34] + 6.0f; 
                validActions.Add(new KeyValuePair<int, float>(baseId + 34, riichiScore)); 
            }
        }

        float maxLogit = validActions.Max(a => a.Value) / softmaxTemperature;
        float sumExp = 0f;
        
        var expValues = new Dictionary<int, float>();
        foreach (var action in validActions) {
            float exp = Mathf.Exp((action.Value / softmaxTemperature) - maxLogit);
            expValues[action.Key] = exp;
            sumExp += exp;
        }

        List<DiscardInfo> discardList = new List<DiscardInfo>();

        foreach (int baseId in uniqueBaseIds)
        {
            bool tenpai = canRiichiDict[baseId];
            float pNorm = (expValues[baseId] / sumExp) * 100f;
            float pRiichi = tenpai ? (expValues[baseId + 34] / sumExp) * 100f : 0f;
            float pTot = pNorm + pRiichi;

            discardList.Add(new DiscardInfo {
                baseTileId = baseId,
                normalProb = pNorm,
                riichiProb = pRiichi,
                totalProb = pTot,
                leadsToTenpai = tenpai
            });
        }

        discardList.Sort((a, b) => b.totalProb.CompareTo(a.totalProb));

        string outputStr = "AI推奨打牌\n\n";
        
        if (!globalCanRiichi) {
            outputStr += "<color=#AAAAAA>※ 現在テンパイしていません</color>\n\n";
        }

        for (int i = 0; i < Mathf.Min(3, discardList.Count); i++)
        {
            var info = discardList[i];
            string name = tileJapaneseNames[info.baseTileId];

            TileType discardTileType = (TileType)info.baseTileId;
            var ukeireInfo = MahjongUtility.GetUkeireInfo(currentHand, discardTileType);
            string shantenText = (ukeireInfo.shanten == 0) ? "テンパイ" : $"{ukeireInfo.shanten}向聴";
            string ukeireText = $" <color=#FFFF00>[{shantenText} / 受入: {ukeireInfo.totalCount}枚]</color>";
            if (info.leadsToTenpai) {
                // 最後に受け入れ枚数のテキストを足す
                outputStr += $"<color=#00FF00>{i + 1}位： {name}</color> (計 {info.totalProb:F1} %)\n{ukeireText}\n";
            } else {
                // 最後に受け入れ枚数のテキストを足す
                outputStr += $"<color=#00FF00>{i + 1}位： {name}</color> ({info.totalProb:F1} %)\n{ukeireText}\n";
            }
        }

        resultText.text = outputStr;

        if (recommendedTileImage != null && discardList.Count > 0)
        {
            recommendedTileImage.sprite = tileSprites[discardList[0].baseTileId];
            recommendedTileImage.enabled = true;
        }
        if (resultBackgroundPanel != null) resultBackgroundPanel.SetActive(true);

        // 第1候補のデータを取得してGASへ投げる
        if (discardList.Count > 0)
        {
            string bestTileName = tileJapaneseNames[discardList[0].baseTileId];
            float bestProb = discardList[0].totalProb;

            TileType bestTileType = (TileType)discardList[0].baseTileId;
            var ukeireInfo = MahjongUtility.GetUkeireInfo(currentHand, bestTileType);
            string ukeireStr = MahjongUtility.CreateUkeirePromptText(ukeireInfo);
            
            // コルーチンをスタート
            StartCoroutine(RequestGasExplanation(promptHandStr, bestTileName, bestProb, ukeireStr));
        }
    }

    // ==========================================
    // 💡 テンパイ判定ロジック群
    // ==========================================
    bool IsTenpai13(List<TileType> hand13)
    {
        for (int i = 0; i < 34; i++) {
            int[] counts = new int[34];
            foreach (var t in hand13) {
                int id = (int)t;
                if (id == 34) id = 13;
                if (id == 35) id = 22;
                counts[id]++;
            }
            counts[i]++; 

            if (IsChiitoitsu(counts)) return true;
            if (IsKokushiMusou(counts)) return true;
            if (CheckNormalAgari(counts, 4)) return true; 
        }
        return false;
    }

    bool CheckNormalAgari(int[] counts, int requiredMentsu)
    {
        for (int i = 0; i < 34; i++) {
            if (counts[i] >= 2) {
                counts[i] -= 2;
                if (CheckMentsu(counts, 0, requiredMentsu)) {
                    counts[i] += 2;
                    return true;
                }
                counts[i] += 2;
            }
        }
        return false;
    }

    bool CheckMentsu(int[] counts, int startIndex, int requiredMentsu)
    {
        if (requiredMentsu == 0) return true;
        int i = startIndex;
        while (i < 34 && counts[i] == 0) i++;
        if (i >= 34) return false;

        if (counts[i] >= 3) {
            counts[i] -= 3;
            if (CheckMentsu(counts, i, requiredMentsu - 1)) {
                counts[i] += 3; return true;
            }
            counts[i] += 3;
        }
        if (i < 27 && (i % 9) < 7) {
            if (counts[i] > 0 && counts[i + 1] > 0 && counts[i + 2] > 0) {
                counts[i]--; counts[i + 1]--; counts[i + 2]--;
                if (CheckMentsu(counts, i, requiredMentsu - 1)) {
                    counts[i]++; counts[i + 1]++; counts[i + 2]++; return true;
                }
                counts[i]++; counts[i + 1]++; counts[i + 2]++;
            }
        }
        return false;
    }

    // 💡 カメラ画像から受け取った牌リストをセットする関数
    public void SetHandFromImage(List<TileType> rawTiles)
    {
        ClearHand(); 

        int count = Mathf.Min(14, rawTiles.Count);
        for (int i = 0; i < count; i++)
        {
            currentHand.Add(rawTiles[i]);
        }

        SortHand(currentHand);
        RefreshHandUI(); 

        if (currentHand.Count == 14)
        {
            AnalyzeHandWithAI();
        }
        else
        {
            resultText.text = $"カメラで {currentHand.Count} 枚読み取りました。（あと {14 - currentHand.Count} 枚）\n足りない牌は手動でクリックして追加してください！";
            if (resultBackgroundPanel != null) resultBackgroundPanel.SetActive(true);
        }
    }
    // --- ここから追加 ---
    bool IsChiitoitsu(int[] counts) {
        int pairCount = 0;
        for (int i = 0; i < 34; i++) {
            if (counts[i] == 2) pairCount++;
            else if (counts[i] > 0) return false;
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

    // 💡 手牌リストをLLMが理解しやすい「19萬 123筒 456索 東東」の形式に変換する関数
    public string ConvertHandToPromptString(List<TileType> hand)
    {
        string manzu = "";
        string pinzu = "";
        string souzu = "";
        string zihai = "";

        // 念のためコピーしてソートする（バラバラに入力されても綺麗に並べるため）
        List<TileType> sortedHand = new List<TileType>(hand);
        sortedHand.Sort((a, b) => ((int)a).CompareTo((int)b));

        foreach (TileType tile in sortedHand)
        {
            int t = (int)tile;
            
            if (t >= 0 && t <= 8) // 萬子 (0-8)
            {
                manzu += (t + 1).ToString();
            }
            else if (t >= 9 && t <= 17) // 筒子 (9-17)
            {
                pinzu += (t - 9 + 1).ToString();
            }
            else if (t >= 18 && t <= 26) // 索子 (18-26)
            {
                souzu += (t - 18 + 1).ToString();
            }
            else if (t >= 27 && t <= 33) // 字牌 (27-33)
            {
                string[] zihaiNames = { "東", "南", "西", "北", "白", "發", "中" };
                zihai += zihaiNames[t - 27];
            }
        }

        string result = "";
        if (!string.IsNullOrEmpty(manzu)) result += manzu + "萬 ";
        if (!string.IsNullOrEmpty(pinzu)) result += pinzu + "筒 ";
        if (!string.IsNullOrEmpty(souzu)) result += souzu + "索 ";
        if (!string.IsNullOrEmpty(zihai)) result += zihai;

        return result.Trim(); // 最後の余計な空白を消して返す
    }



    // ==========================================
    // 💡 GAS通信用のデータ構造（チャット対応版）
    // ==========================================
    [System.Serializable]
    public class ChatMessage {
        public string role;    // "user"（あなた） または "assistant"（講師）
        public string content; // 発言内容
    }

    [System.Serializable]
    public class GasRequest {
        public string token;
        public string mode;        // "test", "review", または "chat"（💡 新追加）
        public string situation;
        public string hand;
        public string aiTile;
        public float aiProb;
        public string aiUkeire;
        public string playerTile;
        public float playerProb;
        public string playerUkeire;
        
        public List<ChatMessage> history; // 💡 新追加：今までの会話履歴
    }

    [System.Serializable]
    public class GasResponse {
        public string result;
    }

    // 💡 TextUIManager 用の通信メソッド
    private IEnumerator RequestGasExplanation(string handString, string bestTileName, float winProb, string ukeireDataStr)
    {
        if (explanationScrollView != null) explanationScrollView.SetActive(true);
        explanationText.text = "<color=#F1C40F>🤖 AI講師が分析中...</color>";

        // 💡 サーバーに送る「材料」だけをセットする（プロンプトは書かない！）
        // 💡 整理されたデータをセットする
        GasRequest reqData = new GasRequest {
            token = "mahjong_secret_2026",
            mode = "test",                 
            hand = handString,
            aiTile = bestTileName,
            aiProb = winProb,
            aiUkeire = ukeireDataStr,
            situation = "",
            playerTile = "",
            playerProb = 0f,
            playerUkeire = ""
        };

        string jsonPayload = JsonUtility.ToJson(reqData);

        // ⚠️ ご自身の新しいGASのウェブアプリURLを貼り付けてください！
        string gasUrl = "https://script.google.com/macros/s/AKfycbwYp9Sdvaae-wN1QHXWpt6KYjhGrW7quo2xXNStZzu_o97xTblhtOOEVask67ubma4BOg/exec"; 

        using (UnityWebRequest request = new UnityWebRequest(gasUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "text/plain");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"GAS通信エラー: {request.error}\n{request.downloadHandler.text}");
                explanationText.text = "<color=#FF5555>⚠️ サーバーとの通信に失敗しました。</color>";
            }
            else
            {
                GasResponse resData = JsonUtility.FromJson<GasResponse>(request.downloadHandler.text);
                
                // 不正アクセスやエラーのチェック
                if (resData.result.Contains("error") || resData.result.Contains("不正なアクセス"))
                {
                    Debug.LogError($"GASエラー詳細:\n{resData.result}");
                    explanationText.text = "<color=#FF5555>⚠️ サーバーでエラーが発生しました。</color>";
                }
                else
                {
                    explanationText.text = $"<color=#3498DB>💡 講師の解説：</color>\n{resData.result}";
                }
            }
        }
    }
}