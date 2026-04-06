using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem; // 💡 これを一番上に追加！
using System.Collections;
using System.Collections.Generic;
using TMPro; //
using System.Linq;
using UnityEngine.Networking;
using System.Text;
public class GameManager : MonoBehaviour
{
    public enum GameState { Init, PlayerTurn, CpuTurn, CheckInterrupt }

    // 💡 修正：振り返り用データ構造（当時の盤面状態を追加）
    [System.Serializable]
    public class ReviewData {
        public string kyokuName;
        public int turn;
        public TileType playerDiscard;
        public TileType aiDiscard;
        public float playerProb;
        public float aiProb;
        public float diff;
        public bool isPlayerRiichi;
        public bool isAIRiichi;
        
        // 💡 追加：当時の盤面スナップショット（写真）
        public List<TileType> handSnapshot;   // その時の手牌
        public List<TileType> river0Snapshot; // 自分の河
        public List<TileType> river1Snapshot; // 下家(CPU1)の河
        public List<TileType> river2Snapshot; // 対面(CPU2)の河
        public List<TileType> doraSnapshot;   // ドラ表示牌

        public int[] scoresSnapshot;
    }
    [HideInInspector]
    public List<ReviewData> matchReviewList = new List<ReviewData>();

    // 💡 追加：牌の名前（日本語）
    private readonly string[] tileNames = {
        "一萬", "二萬", "三萬", "四萬", "五萬", "六萬", "七萬", "八萬", "九萬",
        "一筒", "二筒", "三筒", "四筒", "五筒", "六筒", "七筒", "八筒", "九筒",
        "一索", "二索", "三索", "四索", "五索", "六索", "七索", "八索", "九索",
        "東", "南", "西", "北", "白", "發", "中"
    };

    [Header("AI振り返り画面 UI")]
    public GameObject reviewPanel;               // 振り返り画面の親パネル
    public GameObject openReviewButtonObj;       // 💡 追加：「振り返る」ボタン本体
    public TextMeshProUGUI reviewTitleText;      // 「東1局 5巡目」などのテキスト

    [Header("AI解説表示用 (振り返り画面)")]
    public GameObject explanationScrollView; 
    public TextMeshProUGUI explanationText;
    public TMP_InputField chatInputField;
    
    // 盤面再現用パネル
    public Transform reviewHandPanel;            // 当時の手牌
    public Transform reviewRiverPlayer;          // 自分の河
    public Transform reviewRiverCpu1;            // CPU1の河
    public Transform reviewRiverCpu2;            // CPU2の河
    public Transform reviewDoraPanel;            // ドラ表示牌
    
    // 比較用UI
    public Image reviewPlayerDiscardImage;       // 自分が捨てた牌の画像
    public TextMeshProUGUI reviewPlayerProbText; // 「あなた: 〇〇%」のテキスト
    public Image reviewAiDiscardImage;           // AIが推奨した牌の画像
    public TextMeshProUGUI reviewAiProbText;     // 「AI推奨: 〇〇%」のテキスト
    
    // 切り替えボタン
    public Button[] reviewTopButtons = new Button[3]; // Top1, Top2, Top3 のボタン

    [Header("ゲームルール設定")]
    public GameObject ruleSelectionPanel; // 💡 追加：開始時のルール選択画面
    public enum GameLength { Tonpuu, Hanchan }
    public GameLength currentGameLength = GameLength.Tonpuu; // 現在選ばれているルール

    [Header("ゲーム状態")]
    public GameState currentState = GameState.Init;
    public List<TileType> wall = new List<TileType>();
    public List<TileType>[] playerHands = new List<TileType>[3];
    public List<TileType>[] playerRivers = new List<TileType>[3];
    // public List<TileType>[] playerMelds = new List<TileType>[3]; // これを消して以下に変更
    public List<MeldData>[] playerMelds = new List<MeldData>[3];

    [Header("半荘（ゲーム進行）管理")]
    public int currentBakaze = 27; // 場風（27=東, 28=南）
    public int currentKyoku = 1;   // 局（1局, 2局, 3局）
    public int honba = 0;          // 本場（連荘や流局で増える）
    public TextMeshProUGUI kyokuText; // 💡 「東1局 0本場」などを表示するテキスト

    [Header("UI設定 (手牌・河・副露)")]
    public Transform playerHandPanel;
    public Transform playerRiverPanel;
    public Transform cpu1RiverPanel;
    public Transform cpu2RiverPanel;
    public Transform[] meldPanels = new Transform[3]; // 0:Player, 1:CPU1, 2:CPU2
    public List<Sprite> tileSprites;
    public Transform cpu1HandPanel; // 💡 追加：CPU1の手牌パネル
    public Transform cpu2HandPanel; // 💡 追加：CPU2の手牌パネル
    public Sprite tileBackSprite;   // 💡 追加：伏せ牌（背面）の画像
    // 💡 GameManagerクラスの上のほうに変数を追加
    private TileType? forceCpuDiscardTile = null;

    [Header("AI設定 (Dual Brain)")]
    public SanmaAIBrain aiBrain;

    [Header("ドラ・王牌設定")]
    public List<TileType> deadWall = new List<TileType>(); // 💡 王牌（18枚）
    public int doraCount = 1; // 💡 現在めくられているドラの数
    public Transform doraPanel; // 💡 ドラ表示UI用の親パネル
    
    [Header("スコア管理")]
    public int[] playerScores = new int[3] { 35000, 35000, 35000 }; // 三麻の初期点（3万5千点持ち）
    public int currentOyaIndex = 0; // 現在の親（初期はプレイヤー0）

    [Header("供託（リーチ棒）")]
    public int kyoutaku = 0; // 場に出ている1000点棒の総数
    public Transform kyoutakuPanel; // 💡 過去の局から持ち越された供託用（中央など）
    public Transform[] riichiStickPanels = new Transform[3]; // 💡 追加：0:自分, 1:CPU1, 2:CPU2 のリーチ棒置き場
    public Sprite riichiStickSprite;

    [Header("UI設定 (スコア・リザルト演出)")] // ← 既存の行
    public Transform resultDoraPanel;    // 💡 追加: アガリ画面の表ドラ枠
    public Transform resultUraDoraPanel; // 💡 追加: アガリ画面の裏ドラ枠

    [Header("UI設定 (スコア・リザルト演出)")]
    public TextMeshProUGUI[] scoreTexts = new TextMeshProUGUI[3]; 
    public GameObject resultPanel;         
    public TextMeshProUGUI remainingWallText; 
    public TextMeshProUGUI resultYakuText;   // 1列目（役と翻数）
    public TextMeshProUGUI resultScoreText;  // 💡 追加：2列目（点数専用）

    [Header("自ターンのアクションUI")]
    public GameObject tsumoButtonObj;
    public GameObject kitaButtonObj;   // 💡 追加：北抜きボタン
    public GameObject selfKanButtonObj; // 💡 追加：自ターンのカンボタン
    public GameObject riichiButtonObj; // 💡 これを追加！

    [Header("アガリUI設定")]    
    public GameObject ronButtonObj;   // 他家のターンの「ロン」ボタン
    public Transform resultHandPanel;

    [Header("鳴きUI設定")]
    public GameObject ponUIPanel; // 「ポン」「スキップ」ボタンを入れたパネル
    public GameObject ponButtonObj; // 💡 追加：ポンボタン
    public GameObject kanButtonObj; // 💡 追加：カンボタン
    public GameObject skipButtonObj; // 💡 追加：スキップ(スルー)ボタン

    private bool isDeclaringRiichi = false; // 💡 追加：リーチする牌を選んでいる状態

    private enum NakiChoice { None, Pon, Skip, Kan, Ron}
    // アガリを強制的にTrueにするデバッグ用フラグ
    private bool debugForceAgari = false;
    private NakiChoice playerNakiChoice = NakiChoice.None;

    [Header("演出用UI")]
    public TextMeshProUGUI agariText; // 💡 画面中央の「ツモ！」「ロン！」テキスト
    private Coroutine currentHighlightCoroutine; // 光る演出を管理
    private Image highlightedTileImage;          // 今光っている牌
    
    [Header("リーチ管理")]
    public bool[] isRiichi = new bool[3]; 
    public bool[] needsRiichiRotation = new bool[3]; 
    public int[] riichiTileIndices = new int[] { -1, -1, -1 }; 
    public bool[] hasPayedRiichi = new bool[3]; // 💡 追加：リーチ棒二重没収(フリーズ原因)の防止フラグ

    public bool[] isIppatsuChance = new bool[3]; // 一発チャンス中か
    public bool[] isDoubleRiichi = new bool[3];  // ダブルリーチか
    public int[] turnCount = new int[3];         // 自分のターンが何回来たか
    public bool hasAnyMeldOccurred = false;      // 誰かが鳴いた（ポン・カン・北抜き）か
    
    [Header("特殊役フラグ")]
    public bool isRinshan = false;      // 嶺上開花フラグ
    public bool isChankan = false;      // 槍槓フラグ
    public bool isNagashiMangan = false;// 流し満貫成立フラグ

    // 💡 フリテン管理フラグ
    public bool[] riichiMissedFuriten = new bool[3]; // リーチ後の見逃しフリテン
    public bool[] temporaryFuriten = new bool[3];    // 同巡内の見逃しフリテン

    [Header("デバッグ用テストフラグ")]
    public bool forceCpuRiichi = false;
    public bool forceCpuKan = false;

    // 💡 今の局の会話履歴を保存するリスト
    private List<ChatMessage> currentChatHistory = new List<ChatMessage>();
    
    // ==========================================
    // 💡 プレイヤーのUIボタン用メソッド（インスペクターで紐付け直してください）
    // ==========================================
    public void OnPlayerPonClicked() { playerNakiChoice = NakiChoice.Pon; isWaitingForPlayerNaki = false; }
    public void OnPlayerKanClicked() { playerNakiChoice = NakiChoice.Kan; isWaitingForPlayerNaki = false; } // 大明槓用
    public void OnPlayerSkipClicked() { playerNakiChoice = NakiChoice.Skip; isWaitingForPlayerNaki = false; }
    // 鳴き入力待ち用のフラグ
    private bool isWaitingForPlayerNaki = false;
    // 💡 追加：スマホの「次へ」ボタン用フラグ
    private bool isNextKyokuRequested = false;
    // 💡 これをクラスの上の方に追加
    public bool[] hasDrawnTileThisTurn = new bool[3];

    // 💡 追加：UIボタンから呼び出すメソッド
    public void OnNextKyokuClicked()
    {
        isNextKyokuRequested = true;
    }

    public void UpdateScoreUI()
    {
        for (int i = 0; i < 3; i++)
        {
            if (scoreTexts[i] != null) scoreTexts[i].text = playerScores[i].ToString();
        }
    }
    // 💡 山の残り枚数をUIに反映する関数
    public void UpdateWallCountUI()
    {
        if (remainingWallText != null) remainingWallText.text = $"残り: {wall.Count} 枚";
    }

    // 💡 副露（鳴き・北抜き）の情報を保持するクラス
    // 💡 鳴きの種類を定義
    public enum MeldType { Kita, Pon, Ankan, Shouminkan, Daiminkan }

    // 💡 1枚ではなく「1セット（3〜4枚）」を管理するデータに変更
    public class MeldData
    {
        public MeldType type;
        public TileType tile;
        public int discarderIndex;
    }

    void Start()
    {
        if (ponUIPanel != null) ponUIPanel.SetActive(false);

        // 💡 追加：手牌パネルの「勝手に広がる設定」をプログラムから強制的にOFFにする
        System.Action<Transform> setupLayout = (panel) => {
            if (panel == null) return;
            var hlg = panel.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) {
                hlg.childAlignment = TextAnchor.LowerLeft; // 左下寄せ
                hlg.childForceExpandWidth = false;         // 💡 ここがTrueだと牌が減った時に隙間が広がってしまう！
                hlg.childForceExpandHeight = false;
                hlg.childControlWidth = false;             // 💡 スクリプトで指定した牌のサイズ(sizeDelta)を優先する
                hlg.childControlHeight = false;
            }
        };

        setupLayout(playerHandPanel);
        setupLayout(cpu1HandPanel);
        setupLayout(cpu2HandPanel);

        // 💡 追加：振り返り画面のTop1〜3ボタンに、自動で「画面切り替え機能」を割り当てる
        for (int i = 0; i < 3; i++) {
            int index = i; 
            if (reviewTopButtons.Length > i && reviewTopButtons[i] != null) {
                reviewTopButtons[i].onClick.RemoveAllListeners();
                reviewTopButtons[i].onClick.AddListener(() => OpenReview(index));
            }
        }

        if (ruleSelectionPanel != null) {
            ruleSelectionPanel.SetActive(true);
        } else {
            InitializeGame();
        }
    }
    // クラスの上のほうに変数を追加
    private bool forceCpu1ToDiscard1Pin = false;
    private bool forceCpu1ToDiscardSouth = false;

    void Update()
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return;

        // [K] 北抜きテスト：手牌に北を入れてボタン待ち
        if (keyboard.kKey.wasPressedThisFrame) {
            Debug.Log("デバッグ：北を入れました。「北抜き」ボタンを押してください");
            playerHands[0].Clear();
            for (int i = 0; i < 3; i++) playerHands[0].Add((TileType)30);
            for (int i = 0; i < 11; i++) playerHands[0].Add((TileType)(i + 15));
            SortHand(playerHands[0]);
            RenderPlayerHand();

            UpdateActionUI(); // 💡 これを追加！（AキーやSキーの最後にも追加してください）
        }
        // [P] キーの処理
        if (keyboard.pKey.wasPressedThisFrame)
        {
            Debug.Log("デバッグ：自分を対子にし、CPU1に強制的に1筒を捨てさせる予約をしました！");
            playerHands[0].Clear();
            playerHands[0].Add((TileType)9); playerHands[0].Add((TileType)9); // 1筒2枚
            for (int i = 0; i < 12; i++) playerHands[0].Add((TileType)(i + 15)); // 15~25の適当な牌
            SortHand(playerHands[0]);
            RenderPlayerHand();

            // 💡 確実にフラグを立てる
            forceCpu1ToDiscard1Pin = true;
        }

        // Kキーを押したら次のCPUターンで強制暗槓
        if (keyboard.kKey.wasPressedThisFrame)
        {
            forceCpuKan = true;
            Debug.Log("🔧 デバッグ: 次のCPUターンで強制暗槓を指示しました！");
        }

        // [S] 加槓テスト：ポン状態を作り、手牌に4枚目を入れてボタン待ち
        if (keyboard.sKey.wasPressedThisFrame) {
            Debug.Log("デバッグ：白をポンした状態で手牌に4枚目を入れました。「カン」ボタンを押してください");
            playerMelds[0].Add(new MeldData { type = MeldType.Pon, tile = (TileType)31, discarderIndex = 1 });
            playerHands[0].Clear();
            playerHands[0].Add((TileType)31); // 4枚目
            for (int i = 0; i < 10; i++) playerHands[0].Add((TileType)(i + 15));
            SortHand(playerHands[0]);
            RenderMelds(0);
            RenderPlayerHand();
            UpdateActionUI(); // 💡 これを追加！（AキーやSキーの最後にも追加してください）
        }

        // 💡 [C] 大明槓のテスト（強化版）
        if (keyboard.cKey.wasPressedThisFrame) {
            Debug.Log("デバッグ：自分を暗刻にし、CPU1に強制打牌させます。「カン」UIが出ます");
            playerHands[0].Clear();
            for (int i=0; i<3; i++) playerHands[0].Add((TileType)9); // 1筒3枚
            for (int i=0; i<11; i++) playerHands[0].Add((TileType)(i + 15));
            SortHand(playerHands[0]);
            RenderPlayerHand();
            
            // CPU1に1筒を捨てさせるフラグ（前回作ったもの）
            forceCpu1ToDiscard1Pin = true; 
        }
        // Rキーを押したら次のCPUターンで強制リーチ
        if (keyboard.rKey.wasPressedThisFrame)
        {
            forceCpuRiichi = true;
            Debug.Log("🔧 デバッグ: 次のCPUターンで強制リーチを指示しました！");
        }
        // 💡 [F] 「リーチ牌が鳴かれるシフト」のテスト（激レアケースの再現）
        if (keyboard.fKey.wasPressedThisFrame) {
            Debug.Log("デバッグ：CPU1をリーチさせ、1筒を捨てさせます。それをポンしてください。");
            isRiichi[1] = true;
            needsRiichiRotation[1] = true;
            
            // 自分がポンできるように1筒の対子を持つ
            playerHands[0].Clear();
            playerHands[0].Add((TileType)9); playerHands[0].Add((TileType)9);
            for (int i=0; i<11; i++) playerHands[0].Add((TileType)(i + 15));
            SortHand(playerHands[0]);
            RenderPlayerHand();

            forceCpu1ToDiscard1Pin = true; 
        }
        // 💡 [W] アガリ（Win）テスト：次のツモや他家の打牌で強制的にアガリ判定をTrueにする
        if (keyboard.wKey.wasPressedThisFrame) {
            debugForceAgari = !debugForceAgari;
            Debug.Log($"デバッグ：強制アガリ判定を {debugForceAgari} にしました");
        }
        // 💡 [T] テンパイテスト（大三元・字一色）
        if (keyboard.tKey.wasPressedThisFrame) {
            Debug.Log("デバッグ：白・發・中のテンパイ状態にします");
            playerHands[0].Clear();
            playerHands[0].Add((TileType)31); playerHands[0].Add((TileType)31); playerHands[0].Add((TileType)31); // 白3
            playerHands[0].Add((TileType)32); playerHands[0].Add((TileType)32); playerHands[0].Add((TileType)32); // 發3
            playerHands[0].Add((TileType)33); playerHands[0].Add((TileType)33); playerHands[0].Add((TileType)33); // 中3
            playerHands[0].Add((TileType)27); playerHands[0].Add((TileType)27); playerHands[0].Add((TileType)27); // 東3
            playerHands[0].Add((TileType)28); // 南1（アタマ待ち）
            playerHands[0].Add((TileType)0);
            SortHand(playerHands[0]);
            RenderPlayerHand();
            UpdateActionUI();
            // 💡 確実にフラグを立てる
            forceCpu1ToDiscardSouth = true;
        }
        
        // 💡 [H] 平和テスト
        if (keyboard.hKey.wasPressedThisFrame) {
            Debug.Log("デバッグ：筒子の清一色テンパイにします");
            playerHands[0].Clear();
            playerHands[0].Add((TileType)9); playerHands[0].Add((TileType)9); // 1筒x2
            playerHands[0].Add((TileType)9); playerHands[0].Add((TileType)10); playerHands[0].Add((TileType)11); // 1,2,3,筒
            playerHands[0].Add((TileType)12); playerHands[0].Add((TileType)13); playerHands[0].Add((TileType)14);  // 4,5,6筒
            playerHands[0].Add((TileType)15); playerHands[0].Add((TileType)16); playerHands[0].Add((TileType)17);// 7,8,9筒
            playerHands[0].Add((TileType)15); playerHands[0].Add((TileType)16); // 7,8筒
            playerHands[0].Add((TileType)0);  // 1萬
            SortHand(playerHands[0]);
            RenderPlayerHand();
            UpdateActionUI();
        }

    }

    // ==========================================
    // 💡 リセットボタン用のメソッド
    // ==========================================
    public void ResetGame()
    {
        Debug.Log("ゲームをリセットします");
        StopAllCoroutines(); 
        matchReviewList.Clear(); // 💡 追加：前回の試合の振り返りデータを消す
        ClearBoardUI(); // 💡 先ほど作った関数を使う
        for(int i=0; i<3; i++) playerScores[i] = 35000;
        kyoutaku = 0; // 💡 供託リセット
        UpdateKyoutakuUI();

        // 盤面のUIを全てクリア
        foreach (Transform child in playerHandPanel) Destroy(child.gameObject);
        foreach (Transform child in playerRiverPanel) Destroy(child.gameObject);
        if (cpu1RiverPanel != null) foreach (Transform child in cpu1RiverPanel) Destroy(child.gameObject);
        if (cpu2RiverPanel != null) foreach (Transform child in cpu2RiverPanel) Destroy(child.gameObject);
        for (int i = 0; i < 3; i++) {
            if (meldPanels[i] != null) foreach (Transform child in meldPanels[i]) Destroy(child.gameObject);
        }

        // 変数のリセット
        if (ponUIPanel != null) ponUIPanel.SetActive(false);
        isWaitingForPlayerNaki = false;
        playerNakiChoice = NakiChoice.None;
        isExecutingPon = false;
        forceCpu1ToDiscard1Pin = false;

        // ゲームを最初からやり直す
        InitializeGame();
    }

    void InitializeGame()
    {
        GenerateWall();
        ShuffleWall();

        // 💡 王牌（ワンパイ）を18枚確保
        deadWall.Clear();
        for (int i = 0; i < 18; i++) {
            deadWall.Add(wall[wall.Count - 1]);
            wall.RemoveAt(wall.Count - 1);
        }
        doraCount = 1; // 初期状態は1枚めくれている
        

        for(int i=0; i<3; i++) { 
            isRiichi[i] = false; 
            needsRiichiRotation[i] = false; 
            riichiTileIndices[i] = -1;
            hasPayedRiichi[i] =false;
            riichiMissedFuriten[i] = false;
            temporaryFuriten[i] = false;
            
            // 💡 追加：一発・ダブリーのリセット
            isIppatsuChance[i] = false;
            isDoubleRiichi[i] = false;
            turnCount[i] = 0;
        }
        hasAnyMeldOccurred = false; // 鳴き監視もリセット
        for (int i = 0; i < 3; i++)
        {
            playerHands[i] = new List<TileType>();
            playerRivers[i] = new List<TileType>();
            playerMelds[i] = new List<MeldData>();
        }

        for (int i = 0; i < 3; i++) {
            riichiMissedFuriten[i] = false;
            temporaryFuriten[i] = false;
        }

        DealInitialHands();
        RenderDoraUI(); // 💡 ゲーム開始時にドラを表示
        UpdateKyokuUI();
        UpdateWallCountUI();
        UpdateScoreUI();
        StartPlayerTurn();
    }

    void GenerateWall()
    {
        wall.Clear();
        for (int i = 0; i < 34; i++)
        {
            if (i >= 1 && i <= 7) continue; // 三麻の萬子抜き
            
            int count = 4;
            if (i == 13 || i == 22) count = 3; // 5筒(13)と5索(22)は、通常牌を3枚にする
            for (int c = 0; c < count; c++) wall.Add((TileType)i);
        }
        // 💡 赤ドラを1枚ずつ追加 (34: 赤5筒, 35: 赤5索 として扱います)
        wall.Add((TileType)34);
        wall.Add((TileType)35);
    }

    

    void ShuffleWall()
    {
        for (int i = wall.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            TileType temp = wall[i]; wall[i] = wall[j]; wall[j] = temp;
        }
    }

    void DealInitialHands()
    {
        for (int p = 0; p < 3; p++)
        {
            for (int i = 0; i < 13; i++) DrawTile(p);
            SortHand(playerHands[p]);
            hasDrawnTileThisTurn[p] = false; // 💡 追加：配牌時は隙間を作らない
        }
        RenderCpuHands(); 
    }

    void DrawTile(int playerIndex)
    {
        if (wall.Count == 0) return;
        TileType drawnTile = wall[0];
        wall.RemoveAt(0);
        playerHands[playerIndex].Add(drawnTile);
        
        hasDrawnTileThisTurn[playerIndex] = true; // 💡 追加：ツモってきた直後であることを記録！
        
        UpdateWallCountUI(); 
        if (playerIndex != 0) RenderCpuHands(); 
    }

    // ==========================================
    // 💡 プレイヤーのターン
    // ==========================================
    void StartPlayerTurn()
    {
        currentState = GameState.PlayerTurn;
        isRinshan = false;
        isChankan = false;
        turnCount[0]++;
        DrawTile(0);
        RenderPlayerHand();
        
        UpdateActionUI(); 

        // 💡 修正：自分がリーチ中なら、入力を待たずに自動ツモ切り処理をスタート！
        if (isRiichi[0]) {
            StartCoroutine(AutoDiscardRoutine());
        } else {
            Debug.Log("あなたのターンです。捨てる牌を選んでください。");
        }
    }

    // 💡 新規追加：リーチ中の自動処理
    IEnumerator AutoDiscardRoutine()
    {
        // 牌を引いた直後のテンポを自然にするため少し待つ
        yield return new WaitForSeconds(0.3f);

        TileType drawnTile = playerHands[0][playerHands[0].Count - 1];

        // 💡 ツモ、北抜き、暗槓のどれかが可能な場合は、オート処理を「一時停止」する！
        bool canTsumo = IsAgari(0, drawnTile, true);
        bool canKita = (drawnTile == (TileType)30);
        bool canKan = CanRiichiAnkan(0, drawnTile);

        if (canTsumo || canKita || canKan) {
            Debug.Log("リーチ中ですが、アクションが可能です。オートツモ切りを停止し入力を待ちます。");
            yield break; // これで止まるので、あとはプレイヤーがUIボタン（または手動でのツモ切り）を押すのを待つ状態になります
        }

        // 何もアクションがなければ、もう少しだけ待ってから自動ツモ切り
        yield return new WaitForSeconds(0.4f); 

        DiscardTile(drawnTile);
    }

    public void DiscardTile(TileType tileToDiscard)
    {
        HideActionUI(); 
        isRinshan = false;
        hasDrawnTileThisTurn[0] = false;
        if (currentState != GameState.PlayerTurn) return;
        // 💡 追加：捨てる直前の手牌を使って、AIと自分の判断の違いを計算・記録する！
        RecordPlayerMove(tileToDiscard);

        if (isRiichi[0] && !needsRiichiRotation[0]) 
        {
            playerHands[0].RemoveAt(playerHands[0].Count - 1);
        }
        else 
        {
            playerHands[0].Remove(tileToDiscard);
        }
        SortHand(playerHands[0]);
        RenderPlayerHand();
        
        playerRivers[0].Add(tileToDiscard);

        if (needsRiichiRotation[0])
        {
            riichiTileIndices[0] = playerRivers[0].Count - 1;
            needsRiichiRotation[0] = false;
            StartCoroutine(RiichiPresentationCoroutine());

            // 💡 修正：ポンされたときの点棒の二重徴収(フリーズ原因)を防ぐ
            if (!hasPayedRiichi[0]) {
                playerScores[0] -= 1000;
                kyoutaku++;
                hasPayedRiichi[0] = true;
                UpdateScoreUI();
                UpdateKyoutakuUI();
            }

            // 💡 追加：リーチ宣言をしたので一発チャンス開始！
            isIppatsuChance[0] = true;
            if (turnCount[0] == 1 && !hasAnyMeldOccurred) isDoubleRiichi[0] = true; // 1巡目で鳴きがなければダブリー！
        }
        else
        {
            // 💡 追加：通常の打牌（リーチ後のツモ切り含む）をした時点で、一発チャンスは終了
            isIppatsuChance[0] = false;
        }

        // 💡 自分が打牌したので「同巡内フリテン」を解除
        temporaryFuriten[0] = false; 

        RenderPlayerRiver(); 
        Debug.Log($"あなたは {tileToDiscard} を捨てました。");

        StartCoroutine(CheckInterrupt(0, tileToDiscard));
    }

    public void ExecuteKitaNuki()
    {
        if (currentState != GameState.PlayerTurn) return;
        HideActionUI(); // 💡 押した瞬間に消す！
        RegisterMeldOccurrence(); // 💡 追加：鳴きが入ったので一発を消す！
        
        TileType kita = (TileType)30; 
        if (!playerHands[0].Contains(kita)) return;

        playerHands[0].Remove(kita);
        playerMelds[0].Add(new MeldData { type = MeldType.Kita, tile = kita, discarderIndex = 0 });

        RenderMelds(0);
        isRinshan = true;
        DrawTile(0); // 嶺上ツモ
        RenderPlayerHand();
        
        UpdateActionUI(); // 嶺上ツモした牌でアガれるか、また北があるか再判定！
        if (isRiichi[0]) {
            StartCoroutine(AutoDiscardRoutine());
        }
    }

    // ==========================================
    // 💡 CPUのターン（アニメーション＆バグ修正版）
    // ==========================================
    IEnumerator HandleCpuTurn(int cpuIndex, bool drawTile = true)
    {
        currentState = GameState.CpuTurn;
        isRinshan = false;
        isChankan = false;
        if (drawTile) {
            turnCount[cpuIndex]++;
            yield return new WaitForSeconds(0.8f);
            DrawTile(cpuIndex);

            TileType drawnTile = playerHands[cpuIndex][playerHands[cpuIndex].Count - 1];
            if (IsAgari(cpuIndex, drawnTile, true))
            {
                ExecuteAgari(cpuIndex, cpuIndex, drawnTile, true);
                yield break; 
            }
        }

        bool turnEnded = false;
        while (!turnEnded)
        {
            int action = -1; // -1は未決定

            if (cpuIndex == 1 && forceCpu1ToDiscard1Pin) { forceCpu1ToDiscard1Pin = false; action = 9; if (!playerHands[1].Contains((TileType)9)) playerHands[1].Add((TileType)9); }
            else if (cpuIndex == 1 && forceCpu1ToDiscardSouth) { forceCpu1ToDiscardSouth = false; action = 28; if (!playerHands[1].Contains((TileType)28)) playerHands[1].Add((TileType)28); }
            
            else if (isRiichi[cpuIndex])
            {
                TileType tsumoTile = playerHands[cpuIndex][playerHands[cpuIndex].Count - 1];
                yield return new WaitForSeconds(0.8f); 
                
                if (tsumoTile == (TileType)30) {
                    // 💡 修正：ここで直接北抜きを実行してループを再開する
                    playerHands[cpuIndex].Remove(tsumoTile);
                    playerMelds[cpuIndex].Add(new MeldData { type = MeldType.Kita, tile = tsumoTile, discarderIndex = cpuIndex });
                    RenderMelds(cpuIndex);
                    yield return new WaitForSeconds(0.5f);
                    DrawTile(cpuIndex);
                    continue; 
                } else if (CanRiichiAnkan(cpuIndex, tsumoTile)) {
                    // 💡 修正：ここで直接暗槓を実行して嶺上ツモへ移行
                    ExecuteAnkan(cpuIndex, tsumoTile);
                    yield break; 
                } else {
                    action = (int)tsumoTile; // ツモ切り
                }
            }
            else
            {
                float remRatio = Mathf.Clamp01((float)wall.Count / 55f); 
                int rawAction = aiBrain.DecideAction_v5(
                    cpuIndex, playerHands[cpuIndex], playerRivers, playerMelds, isRiichi, 
                    GetVisibleDoraIndicators(), currentKyoku, currentOyaIndex, remRatio, playerScores
                );

                if (forceCpuRiichi && !isRiichi[cpuIndex]) {
                    forceCpuRiichi = false;
                    rawAction = 34 + (int)playerHands[cpuIndex][playerHands[cpuIndex].Count - 1];
                } else if (forceCpuKan) {
                    forceCpuKan = false;
                    TileType targetTile = playerHands[cpuIndex][0];
                    rawAction = 68 + (int)targetTile;
                    while(playerHands[cpuIndex].FindAll(t => t == targetTile).Count < 4) playerHands[cpuIndex].Add(targetTile);
                }

                if (rawAction == 136) 
                {
                    TileType kita = (TileType)30;
                    if (playerHands[cpuIndex].Contains(kita)) {
                        playerHands[cpuIndex].Remove(kita);
                        playerMelds[cpuIndex].Add(new MeldData { type = MeldType.Kita, tile = kita, discarderIndex = cpuIndex });
                        RenderMelds(cpuIndex);
                        yield return new WaitForSeconds(0.5f);
                        DrawTile(cpuIndex);
                        continue;
                    } else {
                        action = (int)playerHands[cpuIndex][playerHands[cpuIndex].Count - 1]; // フェイルセーフ
                    }
                }
                else if (rawAction >= 102) 
                {
                    TileType targetTile = (TileType)(rawAction - 102);
                    ExecuteShouminkan(cpuIndex, targetTile);
                    yield break; 
                }
                else if (rawAction >= 68) 
                {
                    TileType targetTile = (TileType)(rawAction - 68);
                    ExecuteAnkan(cpuIndex, targetTile);
                    yield break; 
                }
                else if (rawAction >= 34) 
                {
                    if (playerScores[cpuIndex] >= 1000) {
                        isRiichi[cpuIndex] = true;
                        needsRiichiRotation[cpuIndex] = true;
                    }
                    action = rawAction - 34; 
                }
                else 
                {
                    action = rawAction; 
                }
            }

            // ==========================================
            // 💡 通常打牌の実行（究極のアニメーション）
            // ==========================================
            if (action != -1)
            {
                int removeIdx = -1;
                
                // 💡 リーチ中で、かつ「今宣言したわけではない」なら強制的に一番右をツモ切り！
                if (isRiichi[cpuIndex] && !needsRiichiRotation[cpuIndex]) {
                    removeIdx = playerHands[cpuIndex].Count - 1;
                } else {
                    removeIdx = playerHands[cpuIndex].FindIndex(t => GetBaseTileId(t) == action);
                    if (removeIdx == -1) removeIdx = playerHands[cpuIndex].Count - 1;
                }

                TileType discardedTile = playerHands[cpuIndex][removeIdx];
                
                // 💡 アニメ1：捨てる牌の場所を「透明」にして空洞を見せる！
                if (cpuIndex == 1) RenderCpuHands(false, false, removeIdx, -1);
                else RenderCpuHands(false, false, -1, removeIdx);

                // 同時に、河には牌をペシッと表示する
                playerRivers[cpuIndex].Add(discardedTile);
                RenderCpuRivers();

                // 💡 アニメ2：手牌に隙間が空いた状態で 0.4秒 待機する
                yield return new WaitForSeconds(0.6f);

                // 💡 アニメ3：手牌から完全に削除してソートし、左にキュッと詰める！
                playerHands[cpuIndex].RemoveAt(removeIdx);
                SortHand(playerHands[cpuIndex]);
                RenderCpuHands();

                if (needsRiichiRotation[cpuIndex])
                {
                    riichiTileIndices[cpuIndex] = playerRivers[cpuIndex].Count - 1;
                    needsRiichiRotation[cpuIndex] = false;
                    StartCoroutine(RiichiPresentationCoroutine());

                    if (!hasPayedRiichi[cpuIndex]) {
                        playerScores[cpuIndex] -= 1000;
                        kyoutaku++;
                        hasPayedRiichi[cpuIndex] = true;
                        UpdateScoreUI();
                        UpdateKyoutakuUI();
                    }

                    isIppatsuChance[cpuIndex] = true;
                    if (turnCount[cpuIndex] == 1 && !hasAnyMeldOccurred) isDoubleRiichi[cpuIndex] = true;
                }
                else
                {
                    isIppatsuChance[cpuIndex] = false;
                }

                temporaryFuriten[cpuIndex] = false; 
                turnEnded = true;

                StartCoroutine(CheckInterrupt(cpuIndex, discardedTile));
            }
        }
    }

    // ==========================================
    // 💡 他家の打牌時の割り込み（ポン・大明槓）
    // ==========================================
    IEnumerator CheckInterrupt(int discarderIndex, TileType discardedTile)
    {
        currentState = GameState.CheckInterrupt;
        int nakiCaller = -1;
        NakiChoice finalChoice = NakiChoice.None;

        // 1. プレイヤー（自分）の判定
        if (discarderIndex != 0)
        {
            bool canRon = IsAgari(0, discardedTile, false);
            int count = playerHands[0].FindAll(t => t == discardedTile).Count;
            // 💡 変更：リーチ中は鳴けないようにブロック
            bool canPonOrKan = count >= 2 && !isRiichi[0];

            // 🛑 フリテンチェック
            if (canRon && IsFuriten(0))
            {
                Debug.Log("🚨 プレイヤーはフリテンのためロンできません！");
                canRon = false; 
            }

            if (canRon || canPonOrKan)
            {
                ponUIPanel.SetActive(true);
                if (ronButtonObj != null) ronButtonObj.SetActive(canRon); 

                // 💡 修正：ロンができる時はポン・カンボタンを非表示にする！
                if (ponButtonObj != null) ponButtonObj.SetActive(!canRon && canPonOrKan);
                if (kanButtonObj != null) kanButtonObj.SetActive(!canRon && canPonOrKan && count >= 3);
                // 💡 追加：スキップボタンを表示する
                if (skipButtonObj != null) skipButtonObj.SetActive(true);
                // 💡 ここに追加！鳴ける牌（最新の捨て牌）を特定して光らせる
                Transform riverPanel = (discarderIndex == 0) ? playerRiverPanel : (discarderIndex == 1 ? cpu1RiverPanel : cpu2RiverPanel);
                Image discardImg = GetLastTileImage(riverPanel);
                if (discardImg != null) currentHighlightCoroutine = StartCoroutine(FlashTileEffect(discardImg));
                isWaitingForPlayerNaki = true;
                playerNakiChoice = NakiChoice.None;

                while (isWaitingForPlayerNaki) yield return null;
                
                ponUIPanel.SetActive(false);
                if (ronButtonObj != null) ronButtonObj.SetActive(false);
                StopTileHighlight();
                // 💡 ロンできたのにしなかった場合 ＝ 見逃しフリテン確定！
                if (canRon && playerNakiChoice != NakiChoice.Ron)
                {
                    if (isRiichi[0]) riichiMissedFuriten[0] = true;
                    else temporaryFuriten[0] = true;
                    Debug.Log("⚠️ アガリを見逃したため、フリテン状態になりました。");
                }

                if (playerNakiChoice != NakiChoice.None && playerNakiChoice != NakiChoice.Skip)
                {
                    nakiCaller = 0;
                    finalChoice = playerNakiChoice;
                }
            }
        }

        // 2. CPUの判定
        if (nakiCaller == -1)
        {
            for (int i = 1; i < 3; i++)
            {
                if (i == discarderIndex) continue;

                if (IsAgari(i, discardedTile, false))
                {
                    if (IsFuriten(i))
                    {
                        Debug.Log($"🚨 CPU{i} はフリテンのためロンできません！");
                    }
                    else
                    {
                        Debug.Log($"🤖 CPU{i}「ロン！」");
                        nakiCaller = i;
                        finalChoice = NakiChoice.Ron;
                        break; 
                    }
                }

                int count = playerHands[i].FindAll(t => t == discardedTile).Count;
                if (count >= 2 && !isRiichi[i])
                {
                    int aiDecision = aiBrain.DecideNaki(
                        i, playerHands[i], playerRivers, playerMelds, isRiichi, 
                        GetVisibleDoraIndicators(), GetVisibleTilesCount(i), discardedTile, discarderIndex
                    );
                    if (aiDecision == 1) { nakiCaller = i; finalChoice = NakiChoice.Pon; break; }
                    if (aiDecision == 2 && count >= 3) { nakiCaller = i; finalChoice = NakiChoice.Kan; break; } 
                }
            }
        }

        // 3. 実行フェーズ
        if (nakiCaller != -1)
        {
            if (finalChoice == NakiChoice.Ron) ExecuteAgari(nakiCaller, discarderIndex, discardedTile, false);
            else if (finalChoice == NakiChoice.Pon) ExecutePon(nakiCaller, discarderIndex, discardedTile);
            else if (finalChoice == NakiChoice.Kan) ExecuteDaiminkan(nakiCaller, discarderIndex, discardedTile);
        }
        else
        {
            if (wall.Count == 0) ExecuteRyukyoku();
            else if (discarderIndex == 0) StartCoroutine(HandleCpuTurn(1));
            else if (discarderIndex == 1) StartCoroutine(HandleCpuTurn(2));
            else if (discarderIndex == 2) StartPlayerTurn();
        }
    }

    private bool isExecutingPon = false; // 二重実行防止フラグ

    void ExecutePon(int callerIndex, int discarderIndex, TileType tile)
    {
        if (isExecutingPon) return; // 💡 既に実行中ならブロック！
        isExecutingPon = true;

        RegisterMeldOccurrence(); // 💡 追加：鳴きが入ったので一発を消す！

        int riverCount = playerRivers[discarderIndex].Count;
        if (riichiTileIndices[discarderIndex] == riverCount - 1)
        {
            // 奪われる牌がまさにリーチ宣言牌だった場合！
            riichiTileIndices[discarderIndex] = -1; // いったんリセット
            needsRiichiRotation[discarderIndex] = true; // 次の打牌を倒すように予約し直す
        }

        playerRivers[discarderIndex].RemoveAt(playerRivers[discarderIndex].Count - 1);
        RenderCpuRivers();
        RenderCpuHands(); // 💡 この1行を追加！（ポン・大明槓で牌が減ったことを反映する）

        playerHands[callerIndex].Remove(tile);
        playerHands[callerIndex].Remove(tile);

        // 🚨 修正：forループを消し、1セットとして1回だけAddする
        playerMelds[callerIndex].Add(new MeldData { type = MeldType.Pon, tile = tile, discarderIndex = discarderIndex });
        
        RenderMelds(callerIndex);

        if (callerIndex == 0) {
            currentState = GameState.PlayerTurn;
            RenderPlayerHand();
            Debug.Log("ポンしました。捨てる牌を選んでください。");
        } else {
            StartCoroutine(HandleCpuTurn(callerIndex, false));
        }

        isExecutingPon = false; // 💡 処理が終わったらロック解除
    }
    // 💡 新規追加：暗槓（アンカン）
    // 💡 大明槓（他家から鳴く）
    // 💡 大明槓（他家から鳴く）
    void ExecuteDaiminkan(int callerIndex, int discarderIndex, TileType tile)
    {
        int riverCount = playerRivers[discarderIndex].Count;
        if (riichiTileIndices[discarderIndex] == riverCount - 1)
        {
            riichiTileIndices[discarderIndex] = -1; 
            needsRiichiRotation[discarderIndex] = true; 
        }

        playerRivers[discarderIndex].RemoveAt(playerRivers[discarderIndex].Count - 1);
        for (int i = 0; i < 3; i++) playerHands[callerIndex].Remove(tile); 

        RegisterMeldOccurrence(); 
        
        playerMelds[callerIndex].Add(new MeldData { type = MeldType.Daiminkan, tile = tile, discarderIndex = discarderIndex });
        
        RenderCpuRivers();
        RenderCpuHands(); 
        RenderMelds(callerIndex);
        isRinshan = true;

        // 💡 修正：ドラを増やしてからツモる
        if (doraCount < 5) doraCount++;      
        RenderDoraUI();   

        if (callerIndex == 0) {
            DrawTile(0);
            RenderPlayerHand();
            UpdateActionUI(); // 💡 修正：これを忘れていたせいでボタンが押せなくなっていました！
            currentState = GameState.PlayerTurn;
        } else {
            StartCoroutine(HandleCpuTurn(callerIndex, true)); 
        }
    }

    // 💡 鳴き（ポン・カン・北抜き）が発生した時の処理
    void RegisterMeldOccurrence()
    {
        hasAnyMeldOccurred = true;
        for (int i = 0; i < 3; i++) isIppatsuChance[i] = false; // 全員の一発チャンス消滅
    }

    // ==========================================
    // 💡 ツモ・ロンの実行処理
    // ==========================================
    public void OnPlayerTsumoClicked()
    {
        if (currentState != GameState.PlayerTurn) return;
        tsumoButtonObj.SetActive(false);
        Debug.Log("ツモ！ゲーム終了です。");
        ExecuteAgari(0, 0, playerHands[0][playerHands[0].Count - 1], true);
    }

    public void OnPlayerRonClicked()
    {
        playerNakiChoice = NakiChoice.Ron;
        isWaitingForPlayerNaki = false;
    }

    // ==========================================
    // 💡 ツモ・ロンの実行処理と点数交換（演出呼び出し対応版）
    // ==========================================
    void ExecuteAgari(int winnerIndex, int loserIndex, TileType winningTile, bool isTsumo)
    {
        StopAllCoroutines();

        // 💡 追加：リーチ宣言牌でロンされた場合、リーチ不成立として1000点を戻す！
        if (!isTsumo && isRiichi[loserIndex] && riichiTileIndices[loserIndex] == playerRivers[loserIndex].Count - 1)
        {
            Debug.Log("🚨 リーチ宣言牌での放銃！リーチは不成立となり1000点を返還します。");
            kyoutaku--;
            playerScores[loserIndex] += 1000;
            isRiichi[loserIndex] = false; // リーチフラグを折る（役にも付かなくなる）
            UpdateKyoutakuUI();
            UpdateScoreUI();
        }
        
        string message = isTsumo ? $"プレイヤー {winnerIndex} のツモ！" : $"プレイヤー {winnerIndex} が プレイヤー {loserIndex} からロン！";
        
        // 🚨【修正箇所】エラーの原因だった配列作成 (counts) を完全に削除しました！
        // すでに CalculateYaku の内部で赤ドラ対応の配列を作っているので、そのまま呼び出します。
        List<string> achievedYaku = CalculateYaku(winnerIndex, playerHands[winnerIndex], winningTile, isTsumo);
        // 💡 追加：アガリ手牌を成形して表示（アガリ牌を一番右に）
        List<TileType> displayHand = new List<TileType>(playerHands[winnerIndex]);
        if (isTsumo) {
            TileType tsumoTile = displayHand[displayHand.Count - 1]; // 最後の牌がツモ牌
            displayHand.RemoveAt(displayHand.Count - 1);
            SortHand(displayHand); // 理牌（綺麗に並べる）
            displayHand.Add(tsumoTile); // ツモ牌を一番右に戻す
        } else {
            SortHand(displayHand); // 理牌
            displayHand.Add(winningTile); // ロン牌を一番右に追加
        }
        RenderResultHand(displayHand);
        // (前略：アガリ手牌の整理・表示処理)

       // ==========================================
        // 💡 翻数と役満の数をカウントする
        // ==========================================
        int totalHan = 0;
        int yakumanCount = 0; 

        foreach (string yaku in achievedYaku) {
            if (yaku.Contains("役満")) {
                // 💡 二倍役満・三倍役満の文字を解析して倍率を足す！
                if (yaku.Contains("二倍")) yakumanCount += 2;
                else if (yaku.Contains("三倍")) yakumanCount += 3;
                else yakumanCount += 1;
            }
            else {
                int start = yaku.IndexOf('(') + 1;
                int end = yaku.IndexOf("翻");
                if (start > 0 && end > start) {
                    totalHan += int.Parse(yaku.Substring(start, end - start));
                }
            }
        }

        // ==========================================
        // 💡 スコア計算（役満重複に対応！）
        // ==========================================
        bool isParent = (winnerIndex == currentOyaIndex);
        int[] scoreDiffs = new int[3];
        int scoreTotal = 0;
        int baseScoreTotal = 0; // 💡 追加：本場を含まない純粋なアガリ点
        int honbaBonus = honba * 1000; 

        if (isParent) {
            int ronScore = 0, tsumoAll = 0;
            if (yakumanCount > 0) {
                ronScore = 48000 * yakumanCount;
                tsumoAll = 24000 * yakumanCount; 
            } else {
                if (totalHan == 1) { ronScore = 2000; tsumoAll = 1000; }
                else if (totalHan == 2) { ronScore = 3000; tsumoAll = 2000; } 
                else if (totalHan == 3) { ronScore = 6000; tsumoAll = 3000; }
                else if (totalHan >= 4 && totalHan <= 5) { ronScore = 12000; tsumoAll = 6000; }   
                else if (totalHan >= 6 && totalHan <= 7) { ronScore = 18000; tsumoAll = 9000; }   
                else if (totalHan >= 8 && totalHan <= 10) { ronScore = 24000; tsumoAll = 12000; } 
                else if (totalHan >= 11 && totalHan <= 12) { ronScore = 36000; tsumoAll = 18000; }
                else if (totalHan >= 13) { ronScore = 48000; tsumoAll = 24000; } 
            }

            if (isTsumo) {
                int finalTsumoAll = tsumoAll + honbaBonus; 
                scoreTotal = finalTsumoAll * 2;
                baseScoreTotal = tsumoAll * 2; // 純粋な点数
                for (int i = 0; i < 3; i++) {
                    if (i == winnerIndex) scoreDiffs[i] += scoreTotal;
                    else scoreDiffs[i] -= finalTsumoAll;
                }
            } else {
                scoreTotal = ronScore + honbaBonus; 
                baseScoreTotal = ronScore; // 純粋な点数
                scoreDiffs[winnerIndex] += scoreTotal;
                scoreDiffs[loserIndex] -= scoreTotal;
            }
        } else {
            int ronScore = 0, tsumoOya = 0, tsumoKo = 0;
            if (yakumanCount > 0) {
                ronScore = 32000 * yakumanCount;
                tsumoOya = 22000 * yakumanCount;
                tsumoKo = 10000 * yakumanCount;
            } else {
                if (totalHan == 1) { ronScore = 1000; tsumoOya = 500; tsumoKo = 500; } 
                else if (totalHan == 2) { ronScore = 2000; tsumoOya = 1000; tsumoKo = 1000; }
                else if (totalHan == 3) { ronScore = 4000; tsumoOya = 3000; tsumoKo = 1000; }
                else if (totalHan >= 4 && totalHan <= 5) { ronScore = 8000; tsumoOya = 5000; tsumoKo = 3000; }   
                else if (totalHan >= 6 && totalHan <= 7) { ronScore = 12000; tsumoOya = 8000; tsumoKo = 4000; }  
                else if (totalHan >= 8 && totalHan <= 10) { ronScore = 16000; tsumoOya = 10000; tsumoKo = 6000; } 
                else if (totalHan >= 11 && totalHan <= 12) { ronScore = 24000; tsumoOya = 16000; tsumoKo = 8000; }
                else if (totalHan >= 13) { ronScore = 32000; tsumoOya = 22000; tsumoKo = 10000; } 
            }

            if (isTsumo) {
                int finalTsumoOya = tsumoOya + honbaBonus;
                int finalTsumoKo = tsumoKo + honbaBonus;
                scoreTotal = finalTsumoOya + finalTsumoKo;
                baseScoreTotal = tsumoOya + tsumoKo; // 純粋な点数
                scoreDiffs[winnerIndex] += scoreTotal;
                for (int i = 0; i < 3; i++) {
                    if (i != winnerIndex) {
                        if (i == currentOyaIndex) scoreDiffs[i] -= finalTsumoOya;
                        else scoreDiffs[i] -= finalTsumoKo;
                    }
                }
            } else {
                scoreTotal = ronScore + honbaBonus;
                baseScoreTotal = ronScore; // 純粋な点数
                scoreDiffs[winnerIndex] += scoreTotal;
                scoreDiffs[loserIndex] -= scoreTotal;
            }
        }

        string rankName = $"{totalHan}翻"; 
        if (yakumanCount > 0) {
            string[] yakumanNames = { "", "役満", "二倍役満", "三倍役満", "四倍役満", "五倍役満", "六倍役満" };
            rankName = yakumanCount < yakumanNames.Length ? yakumanNames[yakumanCount] : $"{yakumanCount}倍役満";
        } else {
            if (totalHan >= 4 && totalHan <= 5) rankName = "満貫";
            else if (totalHan >= 6 && totalHan <= 7) rankName = "跳満";
            else if (totalHan >= 8 && totalHan <= 10) rankName = "倍満";
            else if (totalHan >= 11 && totalHan <= 12) rankName = "三倍満";
            else if (totalHan >= 13) rankName = "数え役満";
        }

        // 💡 修正：本場と供託をそれぞれ文字列にして extraText にまとめる
        int kyoutakuBonus = kyoutaku * 1000;
        scoreDiffs[winnerIndex] += kyoutakuBonus;
        
        string honbaText = honba > 0 ? $"\n<size=30><color=#FFFFFF>+ 本場 {honbaBonus} 点</color></size>" : "";
        string kyoutakuText = kyoutaku > 0 ? $"\n<size=30><color=#FFFFFF>+ 供託リーチ棒 {kyoutakuBonus} 点</color></size>" : "";
        string extraText = honbaText + kyoutakuText;
        
        kyoutaku = 0; 
        UpdateKyokuUI(); 
        UpdateKyoutakuUI(); 

        int yakuCount = achievedYaku.Count;
        int yakuFontSize = 32; 
        if (yakuCount >= 5) yakuFontSize = 26; 
        if (yakuCount >= 8) yakuFontSize = 20; 

        string yakuString = $"<size={yakuFontSize}>" + string.Join("\n", achievedYaku) + "</size>";
        bool isOyaRenchan = (winnerIndex == currentOyaIndex);

        RenderResultDora(isRiichi[winnerIndex]);

        // ✅ 修正後：ツモ/ロンの対象画像を取得し、演出を挟んでからリザルトを出す！
        string effectText = isTsumo ? "ツモ！" : "ロン！";
        Image targetImg = null;
        if (isTsumo) {
            Transform handPanel = (winnerIndex == 0) ? playerHandPanel : (winnerIndex == 1 ? cpu1HandPanel : cpu2HandPanel);
            targetImg = GetLastTileImage(handPanel);
        } else {
            Transform riverPanel = (loserIndex == 0) ? playerRiverPanel : (loserIndex == 1 ? cpu1RiverPanel : cpu2RiverPanel);
            targetImg = GetLastTileImage(riverPanel);
        }

        StartCoroutine(PlayAgariEffectAndShowResult(effectText, targetImg, winnerIndex, scoreDiffs, yakuString, rankName, baseScoreTotal, isOyaRenchan, false, extraText));
    }
    // ==========================================
    // 💡 和了（アガリ）判定アルゴリズム（役判定入り）
    // ==========================================
    bool IsAgari(int playerIndex, TileType additionalTile, bool isTsumo)
    {
        if (debugForceAgari) return true;

        // 💡 カウント配列の作成（赤ドラを通常牌に変換して形を判定する）
        int[] counts = new int[34];
        List<TileType> allTiles = new List<TileType>(playerHands[playerIndex]);
        if (!isTsumo) allTiles.Add(additionalTile);

        foreach (var t in allTiles) {
            int id = (int)t;
            if (id == 34) id = 13; // 赤5筒 -> 通常5筒
            if (id == 35) id = 22; // 赤5索 -> 通常5索
            counts[id]++;
        }

        int requiredMentsu = allTiles.Count / 3;

        bool isValidShape = false;
        if (requiredMentsu == 4 && IsChiitoitsu(counts)) isValidShape = true;
        else if (requiredMentsu == 4 && IsKokushiMusou(counts)) isValidShape = true;
        else if (CheckNormalAgari(counts, requiredMentsu)) isValidShape = true;

        if (!isValidShape) return false;

        // 💡 修正：CalculateYaku の引数を変更
        List<string> achievedYaku = CalculateYaku(playerIndex, playerHands[playerIndex], additionalTile, isTsumo);
        
        if (achievedYaku.Count > 0) return true; 
        
        Debug.Log("⚠️ アガリの形ですが、役がありません！（役なし）");
        return false;
    }

    // ==========================================
    // 💡 役の計算とリストアップ
    // ==========================================
    List<string> CalculateYaku(int playerIndex, List<TileType> hand, TileType additionalTile, bool isTsumo)
    {
        List<string> yakuList = new List<string>();

        // 💡 流し満貫の場合は他の役を一切計算せずに即返す！
        if (isNagashiMangan) {
            yakuList.Add("流し満貫 (6翻)");
            return yakuList;
        }

        int[] counts = new int[34];
        List<TileType> allTiles = new List<TileType>(hand);
        if (!isTsumo) allTiles.Add(additionalTile);

        int requiredMentsu = allTiles.Count / 3;

        foreach (var t in allTiles) {
            int id = GetBaseTileId(t);
            counts[id]++;
        }

        bool isMenzen = true;
        foreach (var meld in playerMelds[playerIndex]) {
            if (meld.type == MeldType.Pon || meld.type == MeldType.Daiminkan || meld.type == MeldType.Shouminkan) {
                isMenzen = false; break;
            }
        }

        // ==========================================
        // 💡 状況役（天和・地和・海底・河底・嶺上・槍槓）
        // ==========================================
        if (isMenzen && isTsumo && turnCount[playerIndex] == 1 && !hasAnyMeldOccurred) {
            if (playerIndex == currentOyaIndex) yakuList.Add("天和 (役満)");
            else yakuList.Add("地和 (役満)");
        }

        if (wall.Count == 0 && !isRinshan) {
            if (isTsumo) yakuList.Add("海底摸月 (1翻)");
            else yakuList.Add("河底撈魚 (1翻)");
        }

        if (isTsumo && isRinshan) yakuList.Add("嶺上開花 (1翻)");
        if (!isTsumo && isChankan) yakuList.Add("槍槓 (1翻)");

        // 💡 修正：ダブルリーチと一発の判定
        if (isDoubleRiichi[playerIndex]) yakuList.Add("ダブル立直 (2翻)");
        else if (isRiichi[playerIndex]) yakuList.Add("立直 (1翻)");

        if (isRiichi[playerIndex] && isIppatsuChance[playerIndex]) yakuList.Add("一発 (1翻)");
        if (isMenzen && isTsumo) yakuList.Add("門前清自摸和 (1翻)");

        int winningTileId = GetBaseTileId(additionalTile);
        int jikazeOffset = (playerIndex - currentOyaIndex + 3) % 3;
        int jikaze = 27 + jikazeOffset;

        if (isMenzen && IsPinfu(counts, winningTileId, jikaze, currentBakaze)) {
            yakuList.Add("平和 (1翻)");
        }

        if (HasYakuhai(playerIndex, counts, 31)) yakuList.Add("役牌：白 (1翻)");
        if (HasYakuhai(playerIndex, counts, 32)) yakuList.Add("役牌：發 (1翻)");
        if (HasYakuhai(playerIndex, counts, 33)) yakuList.Add("役牌：中 (1翻)");
        
        if (HasYakuhai(playerIndex, counts, currentBakaze)) {
            string bakazeName = (currentBakaze == 27) ? "東" : "南";
            yakuList.Add($"役牌：場風・{bakazeName} (1翻)");
        }
        
        if (HasYakuhai(playerIndex, counts, jikaze)) {
            string jikazeName = (jikaze == 27) ? "東" : (jikaze == 28 ? "南" : "西");
            yakuList.Add($"役牌：自風・{jikazeName} (1翻)");
        }

        if (IsTanyao(playerIndex, counts)) yakuList.Add("断幺九 (1翻)");

        int suitYaku = CheckSuitYaku(playerIndex, counts);
        if (suitYaku == 1) yakuList.Add(isMenzen ? "混一色 (3翻)" : "混一色 (2翻)");
        if (suitYaku == 2) yakuList.Add(isMenzen ? "清一色 (6翻)" : "清一色 (5翻)");

        if (isMenzen && IsChiitoitsu(counts)) yakuList.Add("七対子 (2翻)");
        if (isMenzen && IsKokushiMusou(counts)) yakuList.Add("国士無双 (役満)");

        // ==========================================
        // 💡 国士無双（十三面待ちの判定追加）
        // ==========================================
        if (isMenzen && IsKokushiMusou(counts)) {
            // アガリ牌を抜いた状態でも13面すべて揃っているか確認
            int[] temp = (int[])counts.Clone();
            temp[winningTileId]--;
            bool is13Wait = true;
            int[] yaochu = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
            foreach(int y in yaochu) {
                if (temp[y] != 1) { is13Wait = false; break; }
            }

            if (is13Wait) yakuList.Add("国士無双十三面待ち (二倍役満)");
            else yakuList.Add("国士無双 (役満)");
        }

        // ==========================================
        // 💡 暗刻系・対々和（四暗刻単騎の判定追加）
        // ==========================================
        if (!IsChiitoitsu(counts) && !IsKokushiMusou(counts))
        {
            int ankouCount = 0, minkouCount = 0;
            foreach (var meld in playerMelds[playerIndex]) {
                if (meld.type == MeldType.Pon || meld.type == MeldType.Daiminkan || meld.type == MeldType.Shouminkan) minkouCount++;
                if (meld.type == MeldType.Ankan) ankouCount++;
            }
            for (int i = 0; i < 34; i++) {
                if (counts[i] >= 3) {
                    if (!isTsumo && winningTileId == i) minkouCount++; else ankouCount++;
                }
            }
            
            if (ankouCount == 4) {
                // アガリ牌が雀頭（2枚）を構成しているなら単騎待ち
                if (counts[winningTileId] == 2) yakuList.Add("四暗刻単騎 (二倍役満)");
                else yakuList.Add(isTsumo ? "四暗刻 (役満)" : "三暗刻 (2翻)"); 
            }
            else if (ankouCount == 3) yakuList.Add("三暗刻 (2翻)");

            if (ankouCount + minkouCount == 4) yakuList.Add("対々和 (2翻)");
        }

        // 💡 役満：老頭系・字一色・緑一色・九蓮宝燈
        bool hasMiddle = false, hasTerminal = false, hasHonor = false;
        for(int i=0; i<34; i++) {
            if (counts[i] > 0) {
                if (i >= 27) hasHonor = true;
                else if (i % 9 == 0 || i % 9 == 8) hasTerminal = true;
                else hasMiddle = true;
            }
        }
        foreach(var m in playerMelds[playerIndex]) {
            if (m.type == MeldType.Kita) continue; // 💡 追加：北抜きは役の構成要素から完全に除外！
            int id = GetBaseTileId(m.tile);
            if (id >= 27) hasHonor = true;
            else if (id % 9 == 0 || id % 9 == 8) hasTerminal = true;
            else hasMiddle = true;
        }

        if (!hasMiddle && !hasTerminal && hasHonor) yakuList.Add("字一色 (役満)");
        if (!hasMiddle && hasTerminal && !hasHonor) yakuList.Add("清老頭 (役満)");
        if (!hasMiddle && hasTerminal && hasHonor) yakuList.Add("混老頭 (2翻)");

        if (IsRyuuiisou(counts, playerMelds[playerIndex])) yakuList.Add("緑一色 (役満)");
        if (isMenzen && IsChuuren(counts)) yakuList.Add("九蓮宝燈 (役満)");

        // 💡 役物：大三元・小三元・四喜和
        int sangenKoutsu = 0, sangenToitsu = 0;
        for (int i = 31; i <= 33; i++) {
            if (HasYakuhai(playerIndex, counts, i)) sangenKoutsu++; else if (counts[i] == 2) sangenToitsu++;
        }
        if (sangenKoutsu == 3) yakuList.Add("大三元 (役満)");
        else if (sangenKoutsu == 2 && sangenToitsu == 1) yakuList.Add("小三元 (2翻)");

        int koutsuWinds = 0, toitsuWinds = 0;
        for(int i=27; i<=30; i++) {
            if (HasYakuhai(playerIndex, counts, i)) koutsuWinds++; else if (counts[i] == 2) toitsuWinds++;
        }
        if (koutsuWinds == 4) yakuList.Add("大四喜 (二倍役満)");
        else if (koutsuWinds == 3 && toitsuWinds == 1) yakuList.Add("小四喜 (役満)");

        // // 💡 暗刻系・対々和
        // if (!IsChiitoitsu(counts) && !IsKokushiMusou(counts))
        // {
        //     int ankouCount = 0, minkouCount = 0;
        //     foreach (var meld in playerMelds[playerIndex]) {
        //         if (meld.type == MeldType.Pon || meld.type == MeldType.Daiminkan || meld.type == MeldType.Shouminkan) minkouCount++;
        //         if (meld.type == MeldType.Ankan) ankouCount++;
        //     }
        //     for (int i = 0; i < 34; i++) {
        //         if (counts[i] >= 3) {
        //             if (!isTsumo && winningTileId == i) minkouCount++; else ankouCount++;
        //         }
        //     }
        //     if (ankouCount == 4) yakuList.Add(isTsumo ? "四暗刻 (役満)" : "三暗刻 (2翻)"); 
        //     else if (ankouCount == 3) yakuList.Add("三暗刻 (2翻)");
        //     if (ankouCount + minkouCount == 4) yakuList.Add("対々和 (2翻)");
        // }

        // 💡 一盃口・二盃口
        if (isMenzen && !IsChiitoitsu(counts)) {
            int peikou = CountIipeikou(counts, requiredMentsu);
            if (peikou == 2) yakuList.Add("二盃口 (3翻)");
            else if (peikou == 1) yakuList.Add("一盃口 (1翻)");
        }

        // 💡 一気通貫
        if (CheckIkki(counts, 9) || CheckIkki(counts, 18)) {
            yakuList.Add(isMenzen ? "一気通貫 (2翻)" : "一気通貫 (1翻)");
        }

        // 💡 チャンタ・ジュンチャン
        bool isJunchan;
        if (CheckChanta(counts, playerMelds[playerIndex], out isJunchan)) {
            if (isJunchan) yakuList.Add(isMenzen ? "純全帯幺九 (3翻)" : "純全帯幺九 (2翻)");
            else yakuList.Add(isMenzen ? "混全帯幺九 (2翻)" : "混全帯幺九 (1翻)");
        }

        // ==========================================
        // 💡 役満フィルター ＆ ドラの追加
        // ==========================================
        bool hasYakuman = false;
        foreach (string yaku in yakuList) {
            if (yaku.Contains("役満")) { hasYakuman = true; break; }
        }

        if (hasYakuman) {
            yakuList.RemoveAll(yaku => !yaku.Contains("役満")); // 役満以外を削除
        } else {
            if (yakuList.Count > 0) {
                int dora = CalculateDora(playerIndex, allTiles, isRiichi[playerIndex]);
                if (dora > 0) yakuList.Add($"ドラ ({dora}翻)");
            }
        }

        return yakuList;
    }

    // 💡 染め手（ホンイツ・チンイツ）の判定
    // 💡 染め手（ホンイツ・チンイツ）の判定
    int CheckSuitYaku(int playerIndex, int[] counts)
    {
        bool hasPinzu = false;
        bool hasSouzu = false;
        bool hasHonors = false;

        // 手牌のチェック
        for (int i = 0; i < 34; i++) {
            if (counts[i] > 0) {
                if (i >= 9 && i < 18) hasPinzu = true;       
                else if (i >= 18 && i < 27) hasSouzu = true; 
                else if (i >= 27) hasHonors = true;          
            }
        }

        // 鳴いた牌のチェック
        foreach (var meld in playerMelds[playerIndex]) {
            if (meld.type == MeldType.Kita) continue; // 💡 追加：北抜きは色判定から完全に無視！
            int i = (int)meld.tile;
            if (i >= 9 && i < 18) hasPinzu = true;
            else if (i >= 18 && i < 27) hasSouzu = true;
            else if (i >= 27) hasHonors = true;
        }

        if (hasPinzu ^ hasSouzu) { 
            if (hasHonors) return 1; // 混一色
            else return 2;           // 清一色
        }
        return 0; 
    }

    // 💡 役牌を持っているか（手牌に3枚あるか、または既に鳴いているか）
    bool HasYakuhai(int playerIndex, int[] counts, int tileIndex)
    {
        if (counts[tileIndex] >= 3) return true; // 暗刻として持っている
        foreach (var meld in playerMelds[playerIndex]) {
            if (meld.type == MeldType.Kita) continue; // 💡 追加：北抜きは刻子として数えない！
            if ((int)meld.tile == tileIndex) return true; // ポンやカンをしている
        }
        return false;
    }

    // 💡 タンヤオ判定（1・9・字牌が一切ないか）
    bool IsTanyao(int playerIndex, int[] counts)
    {
        // ヤオチュウ牌のインデックスリスト
        int[] yaochu = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
        
        // 1. 手牌のチェック
        foreach (int i in yaochu) {
            if (counts[i] > 0) return false; 
        }
        
        // 2. 鳴いた牌のチェック（今回はチーがないため、鳴いた牌がヤオチュウか見るだけ）
        foreach (var meld in playerMelds[playerIndex]) {
            // 💡 追加：北抜き（MeldType.Kita）なら、タンヤオを壊さないのでスルー！
            if (meld.type == MeldType.Kita) continue;
            if (System.Array.IndexOf(yaochu, (int)meld.tile) >= 0) return false;
        }
        
        return true;
    }

    // ==========================================
    // 💡 特殊役判定ヘルパー関数群
    // ==========================================

    bool IsRyuuiisou(int[] counts, List<MeldData> melds) {
        int[] allowed = { 19, 20, 21, 23, 25, 32 }; 
        for (int i = 0; i < 34; i++) {
            if (counts[i] > 0 && System.Array.IndexOf(allowed, i) == -1) return false;
        }
        foreach (var m in melds) {
            if (m.type == MeldType.Kita) continue; // 💡 追加：北抜きは緑一色の判定に影響させない！
            int id = GetBaseTileId(m.tile);
            if (System.Array.IndexOf(allowed, id) == -1) return false;
        }
        return true;
    }

    bool IsChuuren(int[] counts) {
        return CheckChuurenSuit(counts, 9) || CheckChuurenSuit(counts, 18);
    }

    bool CheckChuurenSuit(int[] c, int start) {
        if (c[start] < 3 || c[start+8] < 3) return false; // 1と9が3枚以上あるか
        for(int i = 1; i < 8; i++) if (c[start+i] < 1) return false; // 2〜8が1枚以上あるか
        int total = 0;
        for(int i = 0; i < 9; i++) total += c[start+i];
        return total == 14; // その色の牌だけで14枚構成されているか
    }

    int CountIipeikou(int[] original, int requiredMentsu) {
        for (int i = 9; i < 25; i++) {
            if (i % 9 >= 7) continue;
            if (original[i] >= 2 && original[i+1] >= 2 && original[i+2] >= 2) {
                int[] temp = (int[])original.Clone();
                temp[i]-=2; temp[i+1]-=2; temp[i+2]-=2; // 2セット（一盃口）抜く
                if (CheckNormalAgari(temp, requiredMentsu - 2)) {
                    // もう1つ一盃口があるか（二盃口のチェック）
                    for (int j = i; j < 25; j++) {
                        if (j % 9 >= 7) continue;
                        if (temp[j] >= 2 && temp[j+1] >= 2 && temp[j+2] >= 2) {
                            int[] temp2 = (int[])temp.Clone();
                            temp2[j]-=2; temp2[j+1]-=2; temp2[j+2]-=2;
                            if (CheckNormalAgari(temp2, requiredMentsu - 4)) return 2; // 二盃口
                        }
                    }
                    return 1; // 一盃口
                }
            }
        }
        return 0;
    }

    bool CheckIkki(int[] original, int start) {
        for(int i = 0; i < 9; i++) if(original[start+i] == 0) return false;
        int[] temp = (int[])original.Clone();
        for(int i = 0; i < 9; i++) temp[start+i]--; // 123, 456, 789 を抜く
        
        int sum = 0;
        foreach(int t in temp) sum += t;
        int remMentsu = (sum - 2) / 3; 
        return CheckNormalAgari(temp, remMentsu);
    }

    bool CheckChanta(int[] counts, List<MeldData> melds, out bool isJunchan) {
        isJunchan = false;
        int[] bannedMiddle = { 12, 13, 14, 21, 22, 23 }; 
        foreach(int b in bannedMiddle) if(counts[b] > 0) return false;
        
        bool hasHonor = false;
        for(int i = 27; i < 34; i++) if(counts[i] > 0) hasHonor = true;
        foreach(var m in melds) {
            if (m.type == MeldType.Kita) continue; // 💡 追加：北抜きはチャンタの判定から無視！
            int id = GetBaseTileId(m.tile);
            if(id >= 27) hasHonor = true;
            if(id < 27 && (id % 9 != 0 && id % 9 != 8)) return false; 
        }
        
        // 123 と 789 のシュンツをすべて抜く
        int[] temp = (int[])counts.Clone();
        for(int start = 9; start <= 18; start += 9) { 
            while(temp[start]>0 && temp[start+1]>0 && temp[start+2]>0) {
                temp[start]--; temp[start+1]--; temp[start+2]--;
            }
        }
        for(int start = 15; start <= 24; start += 9) { 
            while(temp[start]>0 && temp[start+1]>0 && temp[start+2]>0) {
                temp[start]--; temp[start+1]--; temp[start+2]--;
            }
        }
        // シュンツを抜いた後に 2,3,7,8 が残っていたらアウト
        for(int i = 0; i < 27; i++) {
            if (temp[i] > 0 && (i % 9 != 0 && i % 9 != 8)) return false; 
        }
        
        // 順子が1つもない場合（すべて1,9,字牌の対子や刻子）は「混老頭・清老頭」なのでチャンタではない
        bool hasSequence = false;
        for(int i = 0; i < 27; i++) {
            if (counts[i] != temp[i]) { hasSequence = true; break; }
        }
        if (!hasSequence) return false;

        isJunchan = !hasHonor;
        return true;
    }

    // 💡 一般形（4面子1雀頭）の判定
    bool CheckNormalAgari(int[] counts, int requiredMentsu)
    {
        // 雀頭（アタマ）を順番に仮定して探索
        for (int i = 0; i < 34; i++)
        {
            if (counts[i] >= 2)
            {
                counts[i] -= 2; // アタマとして仮で抜く
                
                // 残りの牌で指定数の面子が作れるか再帰チェック
                if (CheckMentsu(counts, 0, requiredMentsu))
                {
                    counts[i] += 2; // 戻す
                    return true;    // アガリ！
                }
                
                counts[i] += 2; // アタマの仮定が外れたので戻す
            }
        }
        return false;
    }

    // 💡 面子（メンツ）を抽出する再帰関数（バックトラッキング）
    bool CheckMentsu(int[] counts, int startIndex, int requiredMentsu)
    {
        // ベースケース：必要な面子数が0になれば成立！
        if (requiredMentsu == 0) return true;

        // 枚数が1枚以上ある牌を探す
        int i = startIndex;
        while (i < 34 && counts[i] == 0) i++;

        if (i >= 34) return false; // 牌が足りない

        // パターンA：刻子（コーツ / 3枚同じ牌）として抜けるか？
        if (counts[i] >= 3)
        {
            counts[i] -= 3;
            if (CheckMentsu(counts, i, requiredMentsu - 1))
            {
                counts[i] += 3;
                return true;
            }
            counts[i] += 3; // 失敗したら戻す
        }

        // パターンB：順子（シュンツ / 階段）として抜けるか？
        // 字牌(27以上)ではなく、かつ 8,9 (インデックスを9で割った余りが7以上) ではない場合
        if (i < 27 && (i % 9) < 7)
        {
            // 三麻の「2〜8萬抜け」ルールに対しても、カウントが0なので安全に無視されます
            if (counts[i] > 0 && counts[i + 1] > 0 && counts[i + 2] > 0)
            {
                counts[i]--; counts[i + 1]--; counts[i + 2]--; // シュンツとして抜く
                
                if (CheckMentsu(counts, i, requiredMentsu - 1))
                {
                    counts[i]++; counts[i + 1]++; counts[i + 2]++;
                    return true;
                }
                
                counts[i]++; counts[i + 1]++; counts[i + 2]++; // 失敗したら戻す
            }
        }

        return false;
    }

    // 💡 特殊役：七対子
    bool IsChiitoitsu(int[] counts)
    {
        int pairCount = 0;
        for (int i = 0; i < 34; i++) {
            if (counts[i] == 2) pairCount++;
            else if (counts[i] > 0) return false; // 2枚以外があれば不可
        }
        return pairCount == 7;
    }

    // 💡 特殊役：国士無双
    bool IsKokushiMusou(int[] counts)
    {
        int[] yaochu = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
        bool hasPair = false;

        foreach (int i in yaochu) {
            if (counts[i] == 0) return false;
            if (counts[i] == 2) hasPair = true;
            if (counts[i] > 2) return false;
        }
        return hasPair;
    }

    // ==========================================
    // 💡 特殊役：平和（ピンフ）の完全判定アルゴリズム
    // ==========================================
    bool IsPinfu(int[] originalCounts, int winningTile, int jikaze, int bakaze)
    {
        // アタマ（雀頭）の候補をすべて試す
        for (int p = 0; p < 34; p++)
        {
            if (originalCounts[p] >= 2)
            {
                // 【条件1】アタマが役牌（白・發・中・場風・自風）ならピンフにならない
                if (p >= 31 && p <= 33) continue;
                if (p == bakaze) continue;
                if (p == jikaze) continue;

                int[] temp = (int[])originalCounts.Clone();
                temp[p] -= 2; // アタマを仮で抜く

                bool ryanmenFound = false;
                bool isValid = true;

                // 【条件2】残りの牌がすべて「順子（シュンツ）」で構成されているか？
                // 萬子・筒子・索子（0〜26）を左から順にチェック（貪欲法）
                for (int i = 0; i < 27; i++)
                {
                    if (temp[i] == 0) continue;
                    
                    // 8, 9の牌から順子は作れない（1つ右や2つ右がないため）
                    if (i % 9 >= 7) { isValid = false; break; }

                    while (temp[i] > 0)
                    {
                        // 階段（順子）が作れるかチェック
                        if (temp[i + 1] > 0 && temp[i + 2] > 0)
                        {
                            temp[i]--;
                            temp[i + 1]--;
                            temp[i + 2]--;

                            // 【条件3】アガリ牌がこの順子の「両面待ち（リャンメン）」だったか？
                            // 待ちが一番左（例:789の7）で、かつペンチャン（123の3、789の7の形）でないなら両面
                            if (winningTile == i && (i % 9 != 6)) ryanmenFound = true;
                            // 待ちが一番右（例:123の3）で、かつペンチャンでないなら両面
                            if (winningTile == i + 2 && (i % 9 != 0)) ryanmenFound = true;
                        }
                        else
                        {
                            isValid = false; break; // 順子が作れなければ即失敗
                        }
                    }
                    if (!isValid) break;
                }

                // 字牌（27〜33）が余っていたら失敗（順子にならないため）
                for (int i = 27; i < 34; i++) {
                    if (temp[i] > 0) { isValid = false; break; }
                }

                // すべての条件をクリアし、かつ両面待ちの形が含まれていればピンフ成立！
                if (isValid && ryanmenFound) return true;
            }
        }
        return false;
    }

    // ==========================================
    // 💡 自分のターン用のカンボタン処理（UIから呼ぶ）
    // ==========================================
    public void OnPlayerSelfKanClicked()
    {
        if (currentState != GameState.PlayerTurn) return;
        HideActionUI();

        var groups = new Dictionary<TileType, int>();
        foreach (var t in playerHands[0]) {
            if (!groups.ContainsKey(t)) groups[t] = 0;
            groups[t]++;
        }
        foreach (var kvp in groups) {
            if (kvp.Value == 4) { ExecuteAnkan(0, kvp.Key); return; } // 💡 0番(自分)を渡す
        }
        foreach (var meld in playerMelds[0]) {
            if (meld.type == MeldType.Pon && playerHands[0].Contains(meld.tile)) {
                ExecuteShouminkan(0, meld.tile); return; // 💡 0番(自分)を渡す
            }
        }
    }

    // 💡 引数に callerIndex を追加し、誰でも暗槓できるように共通化！
    void ExecuteAnkan(int callerIndex, TileType tile)
    {
        for (int i = 0; i < 4; i++) playerHands[callerIndex].Remove(tile);
        playerMelds[callerIndex].Add(new MeldData { type = MeldType.Ankan, tile = tile, discarderIndex = callerIndex });
        RenderMelds(callerIndex);
        
        if (doraCount < 5) doraCount++; // 安全のため上限を設定
        RenderDoraUI();  
        RegisterMeldOccurrence(); // 💡 追加：鳴きが入ったので一発を消す！
        isRinshan = true;

        if (callerIndex == 0) {
            DrawTile(0);
            RenderPlayerHand();
            UpdateActionUI(); 
        } else {
            StartCoroutine(HandleCpuTurn(callerIndex, true)); // CPUの嶺上ツモへ
        }
    }

    // 💡 引数に callerIndex を追加し、誰でも加槓できるように共通化！
    // 💡 小明槓（ポンしている牌に4枚目を加える）
    void ExecuteShouminkan(int callerIndex, TileType tile)
    {
        // 💡 修正：まずは「手牌から消してポンを加槓に変える」見ための処理だけを行う
        playerHands[callerIndex].Remove(tile);
        MeldData targetMeld = playerMelds[callerIndex].Find(m => m.type == MeldType.Pon && m.tile == tile);
        if (targetMeld != null) targetMeld.type = MeldType.Shouminkan;
        
        RenderMelds(callerIndex);
        RegisterMeldOccurrence(); 
        
        if (callerIndex == 0) {
            SortHand(playerHands[0]);
            RenderPlayerHand();
        }
        
        // 💡 修正：この時点ではまだ「ドラめくり」も「ツモ」も行わず、チャンカン待ちに入る！
        StartCoroutine(CheckChankanInterrupt(callerIndex, tile));
    }

    IEnumerator CheckChankanInterrupt(int callerIndex, TileType tile)
    {
        currentState = GameState.CheckInterrupt;
        int ronWinner = -1;

        // 1. プレイヤーのチャンカン判定
        if (callerIndex != 0 && IsAgari(0, tile, false) && !IsFuriten(0)) {
            ponUIPanel.SetActive(true);
            if (ronButtonObj != null) ronButtonObj.SetActive(true);
            
            isWaitingForPlayerNaki = true;
            playerNakiChoice = NakiChoice.None;

            while (isWaitingForPlayerNaki) yield return null;
            
            ponUIPanel.SetActive(false);
            if (ronButtonObj != null) ronButtonObj.SetActive(false);

            if (playerNakiChoice == NakiChoice.Ron) {
                ronWinner = 0;
            } else {
                if (isRiichi[0]) riichiMissedFuriten[0] = true;
                else temporaryFuriten[0] = true;
            }
        }
        // 2. CPUのチャンカン判定
        if (ronWinner == -1) {
            for (int i = 1; i < 3; i++) {
                if (i == callerIndex) continue;
                if (IsAgari(i, tile, false) && !IsFuriten(i)) {
                    Debug.Log($"🤖 CPU{i}「槍槓（チャンカン）ロン！」");
                    ronWinner = i; break;
                }
            }
        }

        // 3. 実行フェーズ
        if (ronWinner != -1) {
            isChankan = true; // 槍槓フラグON！
            ExecuteAgari(ronWinner, callerIndex, tile, false);
        } else {
            // 誰もロンしなかったので無事に加槓が成立！
            CompleteShouminkan(callerIndex, tile);
        }
    }

    // 💡 チャンカンされずに小明槓が成功した時の処理
    void CompleteShouminkan(int callerIndex, TileType tile)
    {
        // 💡 修正：手牌から消す処理などは既に ExecuteShouminkan で終わっているので削除

        // 無事にカンが成立したので、ここで初めてドラをめくる！
        if (doraCount < 5) doraCount++;
        RenderDoraUI();   

        isRinshan = true; 

        if (callerIndex == 0) {
            DrawTile(0); // 💡 ここで初めて嶺上牌をツモる！
            RenderPlayerHand();
            UpdateActionUI(); // 💡 追加：ボタン更新
            currentState = GameState.PlayerTurn;
        } else {
            StartCoroutine(HandleCpuTurn(callerIndex, true)); 
        }
    }

    // 💡 自ターンでカン（暗槓・加槓）が可能かチェックする関数
    bool CanSelfKan()
    {
        // 1. 暗槓のチェック
        var groups = new Dictionary<TileType, int>();
        foreach (var t in playerHands[0]) {
            if (!groups.ContainsKey(t)) groups[t] = 0;
            groups[t]++;
        }
        foreach (var kvp in groups) {
            if (kvp.Value == 4) return true;
        }

        // 2. 加槓のチェック
        foreach (var meld in playerMelds[0]) {
            if (meld.type == MeldType.Pon && playerHands[0].Contains(meld.tile)) return true;
        }

        return false;
    }

    public void OnPlayerRiichiClicked()
    {
        if (currentState != GameState.PlayerTurn) return;
        if (isRiichi[0]) return; 
        // if (playerScores[0] < 1000) return; // 💡 1000点無いとリーチできない

        isRiichi[0] = true;
        needsRiichiRotation[0] = true;

        // 💡 修正：リーチ宣言モードに入り、手牌のクリック判定を更新する
        isDeclaringRiichi = true;
        HideActionUI();
        RenderPlayerHand(); 
        Debug.Log("リーチ宣言！ テンパイになる牌を選んで捨ててください。");
    }

    // ==========================================
    // 💡 UI描画系メソッド
    // ==========================================
    void RenderPlayerHand()
    {
        if (playerHandPanel == null) return;
        foreach (Transform child in playerHandPanel) Destroy(child.gameObject);

        for (int i = 0; i < playerHands[0].Count; i++)
        {
            TileType tile = playerHands[0][i];

            // 💡 修正：手牌が「ツモを含めた14枚（または11枚等）」の時だけ、一番右に隙間を空ける
            if (i == playerHands[0].Count - 1 && playerHands[0].Count % 3 == 2 && hasDrawnTileThisTurn[0])
            {
                GameObject spacer = new GameObject("Spacer", typeof(RectTransform));
                spacer.transform.SetParent(playerHandPanel, false);
                spacer.GetComponent<RectTransform>().sizeDelta = new Vector2(10, 47);
            }

            GameObject newTile = new GameObject("HandTile", typeof(Image), typeof(Button));
            newTile.transform.SetParent(playerHandPanel, false);
            
            Image img = newTile.GetComponent<Image>();
            img.sprite = tileSprites[(int)tile];
            // img.preserveAspect = true; // 💡 修正：赤牌など元サイズが違っても歪ませない！

            newTile.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 65);
            
            Button btn = newTile.GetComponent<Button>();
            
            if (isDeclaringRiichi) 
            {
                // 💡 リーチ宣言中：その牌を捨てたらテンパイになるかチェック
                List<TileType> tempHand = new List<TileType>(playerHands[0]);
                tempHand.RemoveAt(i);

                if (IsTenpaiHand(tempHand)) {
                    btn.interactable = true;
                    btn.onClick.AddListener(() => {
                        isDeclaringRiichi = false;
                        isRiichi[0] = true; // 牌を選んで初めて本物のリーチ状態になる
                        needsRiichiRotation[0] = true;
                        DiscardTile(tile);
                    });
                } else {
                    // テンパイにならない牌は暗くして押せなくする！
                    btn.interactable = false;
                    ColorBlock cb = btn.colors;
                    cb.disabledColor = new Color(0.4f, 0.4f, 0.4f, 1f); 
                    btn.colors = cb;
                }
            }
            else if (isRiichi[0]) 
            {
                // すでにリーチ済み：一番右のツモ牌しか切れない
                if (currentState == GameState.PlayerTurn && i == playerHands[0].Count - 1 && playerHands[0].Count % 3 == 2) {
                    btn.interactable = true;
                    btn.onClick.AddListener(() => DiscardTile(tile));
                } else {
                    btn.interactable = false;
                    ColorBlock cb = btn.colors;
                    cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 1f); 
                    btn.colors = cb;
                }
            } 
            else 
            {
                // 通常状態：どれでも捨てられる
                btn.onClick.AddListener(() => DiscardTile(tile));
            }
        }
    }
    // ==========================================
    // 💡 プレイヤーの河を描画する関数
    // ==========================================
    public void RenderPlayerRiver()
    {
        if (playerRiverPanel == null) return;

        // 既存の牌をクリア
        foreach (Transform child in playerRiverPanel) Destroy(child.gameObject);

        // リストの中身を順番に描画
        for (int i = 0; i < playerRivers[0].Count; i++)
        {
            CreateRiverTile(playerRiverPanel, playerRivers[0][i], i, 0);
        }
    }

    // ==========================================
    // 💡 CPU（1と2）の河を描画する関数
    // ==========================================
    public void RenderCpuRivers()
    {
        // CPU1 の描画
        if (cpu1RiverPanel != null)
        {
            foreach (Transform child in cpu1RiverPanel) Destroy(child.gameObject);
            for (int i = 0; i < playerRivers[1].Count; i++)
            {
                CreateRiverTile(cpu1RiverPanel, playerRivers[1][i], i, 1);
            }
        }

        // CPU2 の描画
        if (cpu2RiverPanel != null)
        {
            foreach (Transform child in cpu2RiverPanel) Destroy(child.gameObject);
            for (int i = 0; i < playerRivers[2].Count; i++)
            {
                CreateRiverTile(cpu2RiverPanel, playerRivers[2][i], i, 2);
            }
        }
    }

    // ==========================================
    // 💡 河の牌を1枚生成・配置する共通処理（リーチの回転対応）
    // ==========================================
    void CreateRiverTile(Transform parent, TileType tile, int index, int playerIndex)
    {
        // 河用のサイズ（手牌より少し小さめがオススメです）
        float w = 35f;
        float h = 47f;

        GameObject newTile = new GameObject("RiverTile", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        newTile.transform.SetParent(parent, false);
        
        Image img = newTile.GetComponent<Image>();
        img.sprite = tileSprites[(int)tile];
        // img.preserveAspect = true;

        RectTransform rt = newTile.GetComponent<RectTransform>();
        LayoutElement le = newTile.GetComponent<LayoutElement>();

        // 💡 リーチ宣言牌のインデックスと一致するかチェック！
        if (index == riichiTileIndices[playerIndex])
        {
            // リーチ牌：90度倒して、レイアウト上の「幅」と「高さ」を逆にする
            rt.localEulerAngles = new Vector3(0, 0, 90);
            le.preferredWidth = h; 
            le.preferredHeight = w;
            le.minWidth = h;       // 潰れ防止
            le.minHeight = w;
            rt.sizeDelta = new Vector2(h, w);
        }
        else
        {
            // 通常の牌
            rt.localEulerAngles = Vector3.zero;
            le.preferredWidth = w;
            le.preferredHeight = h;
            le.minWidth = w;       // 潰れ防止
            le.minHeight = h;
            rt.sizeDelta = new Vector2(w, h);
        }
    }

    void RenderMelds(int playerIndex)
    {
        if (meldPanels[playerIndex] == null) return;
        foreach (Transform child in meldPanels[playerIndex]) Destroy(child.gameObject);

        var mainLayout = meldPanels[playerIndex].GetComponent<HorizontalLayoutGroup>();
        if (mainLayout != null) mainLayout.spacing = 10f; // セット間の隙間

        // 💡 修正：CPUの鳴き牌は小さく表示する！
        float tileW = (playerIndex == 0) ? 50f : 35f;
        float tileH = (playerIndex == 0) ? 65f : 47f;

        foreach (MeldData meld in playerMelds[playerIndex])
        {
            // 💡 修正箇所1：typeof(ContentSizeFitter) を追加！
            GameObject setContainer = new GameObject("MeldSet", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement), typeof(ContentSizeFitter));
            setContainer.transform.SetParent(meldPanels[playerIndex], false);

            var setGroup = setContainer.GetComponent<HorizontalLayoutGroup>();
            setGroup.childAlignment = TextAnchor.LowerRight;
            setGroup.spacing = 0f;
            setGroup.childControlWidth = true;
            setGroup.childControlHeight = true;
            setGroup.childForceExpandWidth = false;
            setGroup.childForceExpandHeight = false;

            // 💡 修正箇所2：コンテナが潰れないように中身（牌）に合わせて広げる設定を追加
            var fitter = setContainer.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (meld.type == MeldType.Kita) {
                CreateTile(setContainer, meld.tile, tileW, tileH, false, false);
            }
            else if (meld.type == MeldType.Ankan) {
                CreateTile(setContainer, meld.tile, tileW, tileH, false, true);
                CreateTile(setContainer, meld.tile, tileW, tileH, false, false);
                CreateTile(setContainer, meld.tile, tileW, tileH, false, false);
                CreateTile(setContainer, meld.tile, tileW, tileH, false, true);
            }
            else if (meld.type == MeldType.Daiminkan) { // 💡 大明槓
                int relativePos = (meld.discarderIndex - playerIndex + 3) % 3;
                int rotateIndex = 0; 
                if (relativePos == 1) rotateIndex = 3; // 下家なら一番右
                if (relativePos == 2) rotateIndex = 1; // 対面なら2番目

                for (int i = 0; i < 4; i++) {
                    CreateTile(setContainer, meld.tile, tileW, tileH, (i == rotateIndex), false);
                }
            }
            else { // Pon or Shouminkan
                int relativePos = (meld.discarderIndex - playerIndex + 3) % 3;
                int rotateIndex = 0; 
                if (relativePos == 1) rotateIndex = 2; // 下家
                if (relativePos == 2) rotateIndex = 1; // 対面

                for (int i = 0; i < 3; i++) {
                    bool isRotated = (i == rotateIndex);
                    bool isStacked = (isRotated && meld.type == MeldType.Shouminkan);
                    CreateTile(setContainer, meld.tile, tileW, tileH, isRotated, false, isStacked);
                }
            }
        }
    }

    void CreateTile(GameObject parent, TileType tile, float w, float h, bool isRotated, bool isFaceDown, bool isStacked = false)
    {
        GameObject slot = new GameObject("TileSlot", typeof(RectTransform), typeof(LayoutElement));
        slot.transform.SetParent(parent.transform, false);
        
        LayoutElement le = slot.GetComponent<LayoutElement>();
        le.preferredWidth = isRotated ? h : w;
        le.preferredHeight = isRotated ? w : h;
        
        // 💡 修正箇所：絶対に潰されないように最小サイズ（min）も同じ値で固定する！
        le.minWidth = le.preferredWidth;
        le.minHeight = le.preferredHeight;

        slot.GetComponent<RectTransform>().sizeDelta = new Vector2(le.preferredWidth, le.preferredHeight);

        GameObject imgObj = new GameObject("Image", typeof(RectTransform), typeof(Image));
        imgObj.transform.SetParent(slot.transform, false);
        Image img = imgObj.GetComponent<Image>();
        img.sprite = tileSprites[(int)tile];

        if (isFaceDown) img.color = new Color(0.2f, 0.2f, 0.2f); 

        RectTransform rt = imgObj.GetComponent<RectTransform>();
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h); 

        if (isRotated) {
            rt.localEulerAngles = new Vector3(0, 0, 90);
            if (isStacked) {
                GameObject stackedObj = Instantiate(imgObj, slot.transform);
                stackedObj.name = "StackedImage";
                RectTransform stackedRt = stackedObj.GetComponent<RectTransform>();
                stackedRt.anchoredPosition = new Vector2(0, w); 
            }
        }
    }
    // ==========================================
    // 💡 UIボタンの表示・非表示を管理するモジュール
    // ==========================================
    
    // 全てのアクションボタンを即座に隠す
    void HideActionUI()
    {
        if (tsumoButtonObj != null) tsumoButtonObj.SetActive(false);
        if (kitaButtonObj != null) kitaButtonObj.SetActive(false);
        if (selfKanButtonObj != null) selfKanButtonObj.SetActive(false);
        if (riichiButtonObj != null) riichiButtonObj.SetActive(false); // 💡 追加
        StopTileHighlight();
    }

    // 現在の手牌を判定して、出せるボタンだけを表示する
    void UpdateActionUI()
    {
        HideActionUI(); 

        if (currentState != GameState.PlayerTurn) return;

        // 💡 追加：リーチ中のアクション制限
        if (isRiichi[0]) {
            if (IsAgari(0, playerHands[0][playerHands[0].Count - 1], true)) {
                if (tsumoButtonObj != null) tsumoButtonObj.SetActive(true);
            }
            if (playerHands[0][playerHands[0].Count - 1] == (TileType)30) {
                if (kitaButtonObj != null) kitaButtonObj.SetActive(true);
            }
            if (CanRiichiAnkan(0, playerHands[0][playerHands[0].Count - 1])) {
                if (selfKanButtonObj != null) selfKanButtonObj.SetActive(true);
            }
            return; // リーチ中は他をチェックして出さない
        }

        if (playerHands[0].Contains((TileType)30)) {
            if (kitaButtonObj != null) kitaButtonObj.SetActive(true);
        }
        if (CanSelfKan()) {
            if (selfKanButtonObj != null) selfKanButtonObj.SetActive(true);
        }
        if (IsAgari(0, playerHands[0][playerHands[0].Count - 1], true)) {
            if (tsumoButtonObj != null) tsumoButtonObj.SetActive(true);
        }
        // 💡 修正：テンパイ等の条件を満たした時だけリーチボタンを出す！
        if (CanRiichi(0)) {
            if (riichiButtonObj != null) riichiButtonObj.SetActive(true);
        }
    }
    

    // ==========================================
    // 💡 ドラ計算エンジン
    // ==========================================
    int CalculateDora(int playerIndex, List<TileType> allTiles, bool isRiichi)
    {
        int dora = 0;

        // 1. 赤ドラ
        foreach (var t in allTiles) { if ((int)t == 34 || (int)t == 35) dora++; }
        foreach (var m in playerMelds[playerIndex]) { if ((int)m.tile == 34 || (int)m.tile == 35) dora++; }

        // 2. 北抜きドラ
        foreach (var m in playerMelds[playerIndex]) { if (m.type == MeldType.Kita) dora++; }

        // 3. 表ドラ・裏ドラ
        for (int i = 0; i < doraCount; i++) {
            TileType omoteIndicator = deadWall[4 + i * 2]; // 王牌の5, 7, 9, 11番目が表
            dora += CountTargetDora(playerIndex, allTiles, GetDoraTarget(omoteIndicator));

            if (isRiichi) {
                TileType uraIndicator = deadWall[5 + i * 2]; // リーチ時はその下の 6, 8, 10, 12番目が裏
                dora += CountTargetDora(playerIndex, allTiles, GetDoraTarget(uraIndicator));
            }
        }

        return dora;
    }

    int CountTargetDora(int playerIndex, List<TileType> allTiles, TileType target)
    {
        int count = 0;
        int targetId = (int)target;
        int redTargetId = (targetId == 13) ? 34 : (targetId == 22) ? 35 : -1; // ドラが5筒・5索の時は赤も数える

        foreach (var t in allTiles) {
            if ((int)t == targetId || (int)t == redTargetId) count++;
        }
        foreach (var m in playerMelds[playerIndex]) {
            if (m.type == MeldType.Kita && targetId == 30) { count++; continue; }
            if (m.type != MeldType.Kita && ((int)m.tile == targetId || (int)m.tile == redTargetId)) {
                count += (m.type == MeldType.Ankan || m.type == MeldType.Daiminkan || m.type == MeldType.Shouminkan) ? 4 : 3;
            }
        }
        return count;
    }

    TileType GetDoraTarget(TileType indicator)
    {
        int id = (int)indicator;
        if (id == 34) id = 13; if (id == 35) id = 22; // 表示牌が赤でも次は通常牌

        if (id == 0) return (TileType)8; // 1萬 -> 9萬
        if (id == 8) return (TileType)0; // 9萬 -> 1萬

        if (id >= 9 && id < 18) return (TileType)(9 + (id - 9 + 1) % 9);
        if (id >= 18 && id < 27) return (TileType)(18 + (id - 18 + 1) % 9);
        if (id >= 27 && id < 31) return (TileType)(27 + (id - 27 + 1) % 4);
        if (id >= 31 && id < 34) return (TileType)(31 + (id - 31 + 1) % 3);

        return (TileType)0;
    }

    // 💡 ドラUIの描画
    public void RenderDoraUI()
    {
        if (doraPanel == null) return;
        foreach (Transform child in doraPanel) Destroy(child.gameObject);

        for (int i = 0; i < doraCount; i++) {
            TileType indicator = deadWall[4 + i * 2];
            
            GameObject newTile = new GameObject("DoraTile", typeof(RectTransform), typeof(Image));
            newTile.transform.SetParent(doraPanel, false);
            newTile.GetComponent<Image>().sprite = tileSprites[(int)indicator];
            newTile.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 65);
        }
    }

    // 💡 追加：毎局のリザルト表示時に振り返りボタンを準備する関数
    void PrepareReviewButton()
    {
        if (matchReviewList != null && matchReviewList.Count > 0) {
            matchReviewList.Sort((a, b) => b.diff.CompareTo(a.diff)); // ここでソート！
            if (openReviewButtonObj != null) openReviewButtonObj.SetActive(true);
        }
    }

    // ==========================================
    // 💡 アガリ演出と点数反映のシーケンス
    // ==========================================
    IEnumerator ShowAgariResultSequence(int winnerIndex, int[] scoreDiffs, string yakuString, string rankName, int scoreTotal, bool isOyaRenchan, bool isRyukyoku, string extraText = "")
    {
        if (resultPanel != null) resultPanel.SetActive(true);
        
        // 💡 1列目：役名とランク（満貫など）の表示
        if (resultYakuText != null)
        {
            if (winnerIndex == -1) 
            {
                resultYakuText.text = $"<size=80>流局</size>\n\n{yakuString}\n\n<size=45>{rankName}</size>";
            }
            else 
            {
                resultYakuText.text = $"プレイヤー {winnerIndex} の和了！\n\n{yakuString}";
            }
        }

        // 💡 2列目：点数の表示（アガリの時だけドカンと表示！）
        if (resultScoreText != null)
        {
            if (winnerIndex == -1) 
            {
                resultScoreText.text = ""; // 流局時はカラにする
            }
            else 
            {
                // extraText（リーチ棒の回収ボーナスなどの文字）もここにつなげる
                resultScoreText.text = $"<size=45>{rankName}</size>\n\n<size=60><color=yellow>{scoreTotal} 点</color></size>{extraText}";
            }
        }
        yield return new WaitForSeconds(0.5f);
        yield return new WaitUntil(() => 
            (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame) || 
            (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame) ||
            (UnityEngine.InputSystem.Touchscreen.current != null && UnityEngine.InputSystem.Touchscreen.current.primaryTouch.press.wasPressedThisFrame) ||
            isNextKyokuRequested
        );

        if (resultPanel != null) resultPanel.SetActive(false);

        for (int i = 0; i < 3; i++) {
            playerScores[i] += scoreDiffs[i];
        }
        UpdateScoreUI();

        Debug.Log("点数の移動が完了しました。次局へ移行します。");
        GoToNextKyoku(isOyaRenchan, isRyukyoku);
    }

    // ==========================================
    // 💡 AI連携用のデータ成形ロジック
    // ==========================================
    
    // AIに渡す「現在見えているドラ表示牌」のリストを作成
    public List<TileType> GetVisibleDoraIndicators()
    {
        List<TileType> indicators = new List<TileType>();
        for (int i = 0; i < doraCount; i++) {
            indicators.Add(deadWall[4 + i * 2]); // 表ドラのみ（裏ドラはAIにも見えない）
        }
        return indicators;
    }

    // 💡 AIの推論モデル（特徴量）に使いやすい「自分から見えている牌のカウント配列(34次元)」を生成
    public int[] GetVisibleTilesCount(int myIndex)
    {
        int[] visibleCounts = new int[34];

        // 1. 自分の手牌
        foreach (var t in playerHands[myIndex]) {
            int id = (int)t;
            if (id == 34) id = 13; if (id == 35) id = 22; // 赤ドラは通常牌としてカウント
            visibleCounts[id]++;
        }

        // 2. 全員の河（捨て牌）
        for (int p = 0; p < 3; p++) {
            foreach (var t in playerRivers[p]) {
                int id = (int)t;
                if (id == 34) id = 13; if (id == 35) id = 22;
                visibleCounts[id]++;
            }
        }

        // 3. 全員の副露（鳴き牌・北抜き）
        for (int p = 0; p < 3; p++) {
            foreach (var m in playerMelds[p]) {
                int id = (int)m.tile;
                if (id == 34) id = 13; if (id == 35) id = 22;
                
                if (m.type == MeldType.Kita) visibleCounts[id]++;
                else if (m.type == MeldType.Pon) visibleCounts[id] += 3;
                else visibleCounts[id] += 4; // カン
            }
        }

        // 4. ドラ表示牌
        foreach (var t in GetVisibleDoraIndicators()) {
            int id = (int)t;
            if (id == 34) id = 13; if (id == 35) id = 22;
            visibleCounts[id]++;
        }

        return visibleCounts;
    }

    // ==========================================
    // 💡 テンパイ判定（ノーテン罰符用：役なしの「形テン」も許可）
    // ==========================================
    bool IsTenpai(int playerIndex)
    {
        List<TileType> hand = playerHands[playerIndex];
        int requiredMentsu = (hand.Count + 1) / 3;

        for (int i = 0; i < 34; i++)
        {
            int[] counts = new int[34];
            foreach (var t in hand)
            {
                int id = (int)t;
                if (id == 34) id = 13; // 赤ドラは通常牌としてカウント
                if (id == 35) id = 22;
                counts[id]++;
            }
            counts[i]++; // iを仮のツモ（またはロン）牌として足す

            if (requiredMentsu == 4 && IsChiitoitsu(counts)) return true;
            if (requiredMentsu == 4 && IsKokushiMusou(counts)) return true;
            if (CheckNormalAgari(counts, requiredMentsu)) return true;
        }
        return false;
    }

    // ==========================================
    // 💡 拡張ヘルパー関数（ソート・テンパイ・リーチ判定）
    // ==========================================

    // 💡 1. 赤ドラ(34,35)を「5」の横に正しく並べるためのソート関数
    public void SortHand(List<TileType> hand)
    {
        hand.Sort((a, b) => {
            // 赤5筒(34)は13.5、赤5索(35)は22.5として扱い、通常牌の間に挟み込む
            float valA = ((int)a == 34) ? 13.5f : (((int)a == 35) ? 22.5f : (int)a);
            float valB = ((int)b == 34) ? 13.5f : (((int)b == 35) ? 22.5f : (int)b);
            return valA.CompareTo(valB);
        });
    }

    // 💡 2. 手牌リストを直接テンパイ判定する関数（既存の IsTenpai をこれで上書き・分割）
    bool IsTenpaiHand(List<TileType> hand)
    {
        int requiredMentsu = (hand.Count + 1) / 3;
        for (int i = 0; i < 34; i++) {
            int[] counts = new int[34];
            foreach (var t in hand) { counts[GetBaseTileId(t)]++; }
            counts[i]++; 

            if (requiredMentsu == 4 && IsChiitoitsu(counts)) return true;
            if (requiredMentsu == 4 && IsKokushiMusou(counts)) return true;
            if (CheckNormalAgari(counts, requiredMentsu)) return true;
        }
        return false;
    }

    // 💡 3. リーチできるか（門前・1000点以上・打牌後にテンパイするか）をチェック
    bool CanRiichi(int playerIndex)
    {
        if (isRiichi[playerIndex] || playerScores[playerIndex] < 1000) return false;
        
        foreach (var m in playerMelds[playerIndex]) {
            if (m.type == MeldType.Pon || m.type == MeldType.Daiminkan || m.type == MeldType.Shouminkan) return false;
        }

        List<TileType> hand = new List<TileType>(playerHands[playerIndex]);
        for (int i = 0; i < hand.Count; i++) {
            TileType temp = hand[i];
            hand.RemoveAt(i);
            bool tenpai = IsTenpaiHand(hand); // 1枚捨てた形がテンパイになるか？
            hand.Insert(i, temp);
            if (tenpai) return true; // 1つでもテンパイルートがあればリーチ可能！
        }
        return false;
    }

    // 💡 追加：局のテキストを更新する関数
    // 💡 修正：局のテキストと自風を更新する関数
    // ==========================================
    // 💡 局のテキストと自風を更新する関数
    // ==========================================
    public void UpdateKyokuUI()
    {
        if (kyokuText != null) {
            string bakazeStr = (currentBakaze == 27) ? "東" : "南";
            
            // 自分(Player0)の自風を計算
            int jikazeOffset = (0 - currentOyaIndex + 3) % 3;
            string jikazeStr = (jikazeOffset == 0) ? "東" : (jikazeOffset == 1 ? "南" : "西");

            // 💡 「供託: 1000」の文字を繋げる処理を削除して、元のスッキリした表示に戻します！
            kyokuText.text = $"{bakazeStr}{currentKyoku}局 {honba}本場\n自風: {jikazeStr}";
        }
    }

    // ==========================================
    // 💡 流局（山札切れ）の実行処理
    // ==========================================
    void ExecuteRyukyoku()
    {
        StopAllCoroutines();
        HideActionUI();

        // 💡 流局時はアガリ手牌の表示をクリアする
        if (resultHandPanel != null) {
            foreach (Transform child in resultHandPanel) Destroy(child.gameObject);
        }

        // ==========================================
        // 💡 追加：流し満貫の判定！
        // ==========================================
        int nagashiWinner = -1;
        int[] yaochu = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };

        for (int i = 0; i < 3; i++) {
            bool isNagashi = true;
            if (playerRivers[i].Count == 0) isNagashi = false; // 1枚も捨てていない時は無効
            
            // 1. 捨て牌がすべてヤオチュウ牌か？
            foreach (var t in playerRivers[i]) {
                if (System.Array.IndexOf(yaochu, GetBaseTileId(t)) == -1) { isNagashi = false; break; }
            }
            
            // 2. 自分の捨て牌が他家に鳴かれていないか？
            if (isNagashi) {
                for (int p = 0; p < 3; p++) {
                    if (p == i) continue;
                    foreach (var m in playerMelds[p]) {
                        if (m.discarderIndex == i && m.type != MeldType.Ankan && m.type != MeldType.Kita) {
                            isNagashi = false; break;
                        }
                    }
                }
            }

            if (isNagashi) { nagashiWinner = i; break; }
        }

        if (nagashiWinner != -1) {
            Debug.Log($"🎉 プレイヤー{nagashiWinner} の流し満貫成立！");
            isNagashiMangan = true;
            // アガリ扱いとして処理を委譲（他家からのロン扱いとして全員から点数をもらう）
            ExecuteAgari(nagashiWinner, nagashiWinner, (TileType)0, true); 
            isNagashiMangan = false;
            return; // ここで終了
        }

        // 1. 全員のテンパイを確認
        bool[] isTenpai = new bool[3];
        int tenpaiCount = 0;
        for (int i = 0; i < 3; i++) {
            isTenpai[i] = IsTenpai(i);
            if (isTenpai[i]) tenpaiCount++;
        }

        int[] scoreDiffs = new int[3];
        string resultMessage = "";

        // 2. 三麻のノーテン罰符計算（※場に2000点のルールの例）
        int bappuTotal = 2000; 

        if (tenpaiCount > 0 && tenpaiCount < 3)
        {
            int gain = bappuTotal / tenpaiCount;
            int pay = bappuTotal / (3 - tenpaiCount);

            for (int i = 0; i < 3; i++) {
                if (isTenpai[i]) scoreDiffs[i] += gain;
                else scoreDiffs[i] -= pay;
            }
        }

        // 3. 結果テキストの生成
        for (int i = 0; i < 3; i++) {
            string status = isTenpai[i] ? "<color=yellow>テンパイ</color>" : "<color=#888888>ノーテン</color>";
            string playerName = (i == 0) ? "自分" : $"CPU{i}";
            string scoreStr = "";
            if (tenpaiCount > 0 && tenpaiCount < 3) {
                scoreStr = isTenpai[i] ? $" (+{bappuTotal / tenpaiCount})" : $" (-{bappuTotal / (3 - tenpaiCount)})";
            }
            resultMessage += $"{playerName} : {status}{scoreStr}\n";
        }

        // 💡 追加：流局リザルトを出す直前に、各プレイヤーのリーチ状態を解除して供託（中央）にまとめる！
        for (int i = 0; i < 3; i++) {
            isRiichi[i] = false; 
        }
        UpdateKyoutakuUI();

        // 💡 追加：テンパイしているCPUの手牌を表向きにして公開！
        RenderCpuHands(isTenpai[1], isTenpai[2]);

        bool isOyaRenchan = isTenpai[currentOyaIndex];
        StartCoroutine(ShowAgariResultSequence(-1, scoreDiffs, resultMessage, "流局", 0, isOyaRenchan, true));
    }
    // ==========================================
    // 💡 リザルト画面にアガリ手牌を描画する
    // ==========================================
    void RenderResultHand(List<TileType> hand)
    {
        if (resultHandPanel == null) return;
        foreach (Transform child in resultHandPanel) Destroy(child.gameObject);

        for (int i = 0; i < hand.Count; i++)
        {
            if (i == hand.Count - 1) {
                GameObject spacer = new GameObject("Spacer", typeof(RectTransform));
                spacer.transform.SetParent(resultHandPanel, false);
                spacer.GetComponent<RectTransform>().sizeDelta = new Vector2(15, 65);
            }

            GameObject newTile = new GameObject("ResultTile", typeof(RectTransform), typeof(Image));
            newTile.transform.SetParent(resultHandPanel, false);
            
            Image img = newTile.GetComponent<Image>();
            img.sprite = tileSprites[(int)hand[i]];
            // img.preserveAspect = true; // 💡 修正：歪み防止！

            newTile.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 65);
        }
    }

    // ==========================================
    // 💡 局の進行とゲーム終了処理
    // ==========================================
    // ==========================================
    // 💡 局の進行とゲーム終了処理
    // ==========================================
    void GoToNextKyoku(bool isOyaRenchan, bool isRyukyoku)
    {
        if (isOyaRenchan) {
            honba++; // 親のアガリかテンパイなら連荘（本場が増える）
        } else {
            if (isRyukyoku) honba++; else honba = 0; // 流局なら本場継続、子の和了ならリセット
            
            currentOyaIndex = (currentOyaIndex + 1) % 3; // 親が右に移動
            currentKyoku++;
            
            // 南3局を超えたらどうなるか
            if (currentKyoku > 3) {
                currentKyoku = 1;
                currentBakaze++; // 東場(27)から南場(28)へ
            }
        }

        // 💡 修正：終了判定を if-else の外に出して、「毎局必ず」チェックするようにしました！
        // 終了判定（ハコ下、または規定の局が終わったか）
        bool isHakoshita = playerScores[0] < 0 || playerScores[1] < 0 || playerScores[2] < 0;
        
        // 選んだルールによって「どこで終わるか」を変える
        int endBakaze = (currentGameLength == GameLength.Tonpuu) ? 27 : 28;

        if (currentBakaze > endBakaze || isHakoshita) {
            ShowFinalResult();
            return;
        }

        // 盤面をクリアして次局スタート
        ClearBoardUI();
        InitializeGame();
    }

    void ClearBoardUI()
    {
        foreach (Transform child in playerHandPanel) Destroy(child.gameObject);
        // 💡 追加：CPUの手牌も盤面から消す
        if (cpu1HandPanel != null) foreach (Transform child in cpu1HandPanel) Destroy(child.gameObject);
        if (cpu2HandPanel != null) foreach (Transform child in cpu2HandPanel) Destroy(child.gameObject);
        foreach (Transform child in playerRiverPanel) Destroy(child.gameObject);
        if (cpu1RiverPanel != null) foreach (Transform child in cpu1RiverPanel) Destroy(child.gameObject);
        if (cpu2RiverPanel != null) foreach (Transform child in cpu2RiverPanel) Destroy(child.gameObject);
        for (int i = 0; i < 3; i++) {
            if (meldPanels[i] != null) foreach (Transform child in meldPanels[i]) Destroy(child.gameObject);
        }
        if (resultHandPanel != null) foreach (Transform child in resultHandPanel) Destroy(child.gameObject);
        if (ponUIPanel != null) ponUIPanel.SetActive(false);

        // リザルト画面のテキストとパネルを完全にリセット
        if (resultYakuText != null) resultYakuText.text = "";
        if (resultScoreText != null) resultScoreText.text = ""; // 💡 これを追加！
        if (resultPanel != null) resultPanel.SetActive(false);

        // ClearBoardUIの中の最後にこれを追加
        // 次局へ行くときは、中身を消すだけでなく「パネル自体を非表示」に戻す！
        if (resultDoraPanel != null) {
            resultDoraPanel.gameObject.SetActive(false);
            foreach (Transform child in resultDoraPanel) Destroy(child.gameObject);
        }
        if (resultUraDoraPanel != null) {
            resultUraDoraPanel.gameObject.SetActive(false);
            foreach (Transform child in resultUraDoraPanel) Destroy(child.gameObject);
        }
        
        // 普段は振り返りボタンを隠しておく
        if (openReviewButtonObj != null) openReviewButtonObj.SetActive(false);
    }

    void ShowFinalResult()
    {
        if (resultPanel != null) resultPanel.SetActive(true);
        var rankings = new List<KeyValuePair<string, int>> {
            new KeyValuePair<string, int>("自分", playerScores[0]),
            new KeyValuePair<string, int>("CPU 1", playerScores[1]),
            new KeyValuePair<string, int>("CPU 2", playerScores[2])
        };
        rankings.Sort((a, b) => b.Value.CompareTo(a.Value));

        // 💡 修正：長いテキスト（reviewStr）の生成を丸ごと削除し、スッキリさせました
        if (resultYakuText != null) {
            resultYakuText.text = $"<size=50>🔥 半荘終了 🔥</size>\n\n" +
                                $"<color=yellow><size=50>1位: {rankings[0].Key} ({rankings[0].Value}点)</size></color>\n" +
                                $"2位: {rankings[1].Key} ({rankings[1].Value}点)\n" +
                                $"3位: {rankings[2].Key} ({rankings[2].Value}点)\n\n" +
                                $"<size=36>お疲れ様でした！</size>";
        }

        PrepareReviewButton();
    }

    // 💡 赤ドラ対応の基本牌ID取得（フリテン判定用）
    int GetBaseTileId(TileType tile) {
        int id = (int)tile;
        if (id == 34) return 13; // 赤5筒 -> 5筒
        if (id == 35) return 22; // 赤5索 -> 5索
        return id;
    }

    // 💡 現在の手牌から「待ち牌」のリストを取得する
    List<int> GetMachiTiles(int playerIndex)
    {
        List<int> machiTiles = new List<int>();
        // 0〜33のすべての牌に対して、アガれるかテストする
        for (int i = 0; i < 34; i++)
        {
            TileType testTile = (TileType)i;
            // テンパイしていて、その牌でアガれるなら待ち牌に追加
            if (IsAgari(playerIndex, testTile, false)) 
            {
                machiTiles.Add(i);
            }
        }
        return machiTiles;
    }

    // 💡 現在のプレイヤーが「フリテン状態」かどうかを判定する
    public bool IsFuriten(int playerIndex)
    {
        // 1. リーチ後フリテン
        if (riichiMissedFuriten[playerIndex]) return true;

        // 2. 同巡内フリテン
        if (temporaryFuriten[playerIndex]) return true;

        // 3. 通常フリテン（自分の捨て牌に待ち牌があるか）
        List<int> machiTiles = GetMachiTiles(playerIndex);
        if (machiTiles.Count > 0)
        {
            foreach (var discarded in playerRivers[playerIndex])
            {
                int discardedId = GetBaseTileId(discarded);
                if (machiTiles.Contains(discardedId))
                {
                    return true; // 捨て牌の中に待ち牌があった！
                }
            }
        }
        return false;
    }

    // ==========================================
    // 💡 リザルト画面での表ドラ・裏ドラの描画処理
    // ==========================================
    void RenderResultDora(bool isWinnerRiichi)
    {
        // 💡 表ドラパネルの表示設定
        if (resultDoraPanel != null) {
            resultDoraPanel.gameObject.SetActive(true); // 確実に表示
            foreach (Transform child in resultDoraPanel) Destroy(child.gameObject);
            for (int i = 0; i < doraCount; i++) {
                TileType indicator = deadWall[4 + i * 2]; 
                CreateResultDoraTile(resultDoraPanel, indicator);
            }
        }
        
        // 💡 裏ドラパネル：和了者がリーチしている時のみ表示
        if (resultUraDoraPanel != null) {
            resultUraDoraPanel.gameObject.SetActive(isWinnerRiichi);
            foreach (Transform child in resultUraDoraPanel) Destroy(child.gameObject);
            if (isWinnerRiichi) {
                for (int i = 0; i < doraCount; i++) {
                    TileType uraIndicator = deadWall[5 + i * 2];
                    CreateResultDoraTile(resultUraDoraPanel, uraIndicator);
                }
            }
        }
    }

    void CreateResultDoraTile(Transform parent, TileType tile)
    {
        GameObject newTile = new GameObject("ResultDoraTile", typeof(RectTransform), typeof(Image));
        newTile.transform.SetParent(parent, false);
        newTile.GetComponent<Image>().sprite = tileSprites[(int)tile];
        newTile.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 65); // サイズはお好みで調整
    }

// ==========================================
    // 💡 リーチ棒（供託）を画面に描画する
    // ==========================================
    public void UpdateKyoutakuUI()
    {
        // 1. 全てのパネルの古い画像をクリア
        if (kyoutakuPanel != null) {
            foreach (Transform child in kyoutakuPanel) Destroy(child.gameObject);
        }
        for (int i = 0; i < 3; i++) {
            if (riichiStickPanels[i] != null) {
                foreach (Transform child in riichiStickPanels[i]) Destroy(child.gameObject);
            }
        }

        if (riichiStickSprite == null) return;

        // 💡 もし供託が0なら（アガリ直後やリセット時）、卓上の棒は全て回収されるためここで終了
        if (kyoutaku == 0) return;

        // 2. 現在の局で出されたリーチ棒の数をカウント
        int currentRiichiCount = 0;
        for (int i = 0; i < 3; i++) {
            if (isRiichi[i]) currentRiichiCount++;
        }

        // 3. 過去から持ち越された供託（流局分）を計算して、中央（kyoutakuPanel）に並べる
        int carryOverKyoutaku = kyoutaku - currentRiichiCount;
        if (carryOverKyoutaku > 0 && kyoutakuPanel != null) {
            for (int i = 0; i < carryOverKyoutaku; i++) {
                GameObject stick = new GameObject("RiichiStick_Center", typeof(RectTransform), typeof(Image));
                stick.transform.SetParent(kyoutakuPanel, false);
                stick.GetComponent<Image>().sprite = riichiStickSprite;
                stick.GetComponent<RectTransform>().sizeDelta = new Vector2(120, 18);
            }
        }

        // 4. 今局で宣言した人のリーチ棒を、それぞれの専用パネルに置く！
        for (int i = 0; i < 3; i++) {
            if (isRiichi[i] && riichiStickPanels[i] != null) {
                GameObject stick = new GameObject($"RiichiStick_Player{i}", typeof(RectTransform), typeof(Image));
                stick.transform.SetParent(riichiStickPanels[i], false);
                stick.GetComponent<Image>().sprite = riichiStickSprite;
                stick.GetComponent<RectTransform>().sizeDelta = new Vector2(120, 18);
            }
        }
    }
    // 💡 修正：リーチ中の暗槓が可能かチェックする関数
    bool CanRiichiAnkan(int playerIndex, TileType tsumoTile)
    {
        int tileId = GetBaseTileId(tsumoTile);
        
        List<TileType> hand13 = new List<TileType>(playerHands[playerIndex]);
        hand13.RemoveAt(hand13.Count - 1); // ツモ牌を一旦抜く
        
        int count = hand13.FindAll(t => GetBaseTileId(t) == tileId).Count;
        if (count != 3) return false; // そもそも暗刻がない
        
        List<int> oldMachi = GetMachiTilesForHand(hand13);
        
        List<TileType> hand10 = new List<TileType>(hand13);
        hand10.RemoveAll(t => GetBaseTileId(t) == tileId); // 4枚すべて抜いた10枚の手牌
        List<int> newMachi = GetMachiTilesForHand(hand10);
        
        if (oldMachi.Count == 0 || oldMachi.Count != newMachi.Count) return false;
        foreach (int m in oldMachi) {
            if (!newMachi.Contains(m)) return false;
        }
        return true;
    }

    List<int> GetMachiTilesForHand(List<TileType> hand)
    {
        List<int> machi = new List<int>();
        for (int i = 0; i < 34; i++) {
            List<TileType> testHand = new List<TileType>(hand);
            testHand.Add((TileType)i);
            if (IsTenpaiHand(testHand)) machi.Add(i);
        }
        return machi;
    }
    // ==========================================
    // 💡 CPUの手牌描画ロジック（アニメーション対応版）
    // ==========================================
    public void RenderCpuHands(bool reveal1 = false, bool reveal2 = false, int hideIdx1 = -1, int hideIdx2 = -1)
    {
        RenderSingleCpuHand(1, cpu1HandPanel, reveal1, hideIdx1);
        RenderSingleCpuHand(2, cpu2HandPanel, reveal2, hideIdx2);
    }

    void RenderSingleCpuHand(int cpuIndex, Transform panel, bool reveal, int hideIndex)
    {
        if (panel == null) return;
        foreach (Transform child in panel) Destroy(child.gameObject);

        for (int i = 0; i < playerHands[cpuIndex].Count; i++)
        {
            TileType tile = playerHands[cpuIndex][i];

            if (i == playerHands[cpuIndex].Count - 1 && playerHands[cpuIndex].Count % 3 == 2 && hasDrawnTileThisTurn[cpuIndex])
            {
                GameObject spacer = new GameObject("Spacer", typeof(RectTransform));
                spacer.transform.SetParent(panel, false);
                spacer.GetComponent<RectTransform>().sizeDelta = new Vector2(8, 38);
            }

            GameObject newTile = new GameObject("CpuHandTile", typeof(RectTransform), typeof(Image));
            newTile.transform.SetParent(panel, false);
            
            Image img = newTile.GetComponent<Image>();
            
            // 💡 追加：捨てた牌のインデックス(hideIndex)なら、そこだけ透明にして空洞を作る！
            if (i == hideIndex) {
                img.color = new Color(1f, 1f, 1f, 0f); 
            } else {
                if (reveal) {
                    img.sprite = tileSprites[(int)tile];
                    img.color = Color.white;
                } else {
                    if (tileBackSprite != null) {
                        img.sprite = tileBackSprite;
                        img.color = Color.white;
                    } else {
                        img.sprite = tileSprites[(int)tile];
                        img.color = new Color(0.1f, 0.1f, 0.1f, 1f);
                    }
                }
            }

            newTile.GetComponent<RectTransform>().sizeDelta = new Vector2(35, 47);
        }
    }
    // ==========================================
    // 💡 AI振り返り用データ記録処理
    // ==========================================
    void RecordPlayerMove(TileType actualDiscard)
    {
        // リーチ済み（オートツモ切り状態）なら記録しない
        if (isRiichi[0] && !needsRiichiRotation[0]) return; 

        float remRatio = Mathf.Clamp01((float)wall.Count / 55f);
        float[] rawLogits = aiBrain.GetActionProbabilities_v5(
            0, playerHands[0], playerRivers, playerMelds, isRiichi, 
            GetVisibleDoraIndicators(), currentKyoku, currentOyaIndex, remRatio, playerScores
        );

        var uniqueBaseIds = playerHands[0].Select(t => GetBaseTileId(t)).Distinct().ToList();
        float temperature = 2.0f; 
        var validActions = new Dictionary<int, float>();
        
        bool isMenzen = !playerMelds[0].Any(m => m.type == MeldType.Pon || m.type == MeldType.Daiminkan || m.type == MeldType.Shouminkan);

        foreach (int baseId in uniqueBaseIds)
        {
            validActions[baseId] = rawLogits[baseId]; 
            
            if (isMenzen && !isRiichi[0]) {
                List<TileType> tempHand = new List<TileType>(playerHands[0]);
                int removeIdx = tempHand.FindIndex(t => GetBaseTileId(t) == baseId);
                if (removeIdx != -1) {
                    tempHand.RemoveAt(removeIdx);
                    if (IsTenpaiHand(tempHand)) {
                        validActions[baseId + 34] = rawLogits[baseId + 34] + 6.0f; // 勇気ボーナス
                    }
                }
            }
        }

        if (validActions.Count == 0) return;

        float maxLogit = validActions.Values.Max() / temperature;
        float sumExp = 0f;
        var expValues = new Dictionary<int, float>();
        foreach (var kvp in validActions) {
            float exp = Mathf.Exp((kvp.Value / temperature) - maxLogit);
            expValues[kvp.Key] = exp;
            sumExp += exp;
        }

        int bestActionId = -1;
        float bestProb = -1f;
        foreach (var kvp in expValues) {
            float prob = (kvp.Value / sumExp) * 100f;
            if (prob > bestProb) {
                bestProb = prob;
                bestActionId = kvp.Key;
            }
        }

        int playerActionId = GetBaseTileId(actualDiscard);
        if (needsRiichiRotation[0]) playerActionId += 34; // 今リーチ宣言した

        float playerProb = 0f;
        if (expValues.ContainsKey(playerActionId)) {
            playerProb = (expValues[playerActionId] / sumExp) * 100f;
        } else if (expValues.ContainsKey(GetBaseTileId(actualDiscard))) {
            playerProb = (expValues[GetBaseTileId(actualDiscard)] / sumExp) * 100f;
        }

        TileType aiRecTile = (TileType)(bestActionId % 34);
        string bakazeStr = (currentBakaze == 27) ? "東" : "南";
        string kyokuName = $"{bakazeStr}{currentKyoku}局";

        // 💡 修正：当時のリストの中身をコピー（new List）して丸ごと保存する！
        matchReviewList.Add(new ReviewData {
            kyokuName = kyokuName,
            turn = turnCount[0],
            playerDiscard = actualDiscard,
            aiDiscard = aiRecTile,
            playerProb = playerProb,
            aiProb = bestProb,
            diff = bestProb - playerProb,
            isPlayerRiichi = needsRiichiRotation[0],
            isAIRiichi = (bestActionId >= 34 && bestActionId < 68),
            
            // 👇 ここを追加！
            handSnapshot = new List<TileType>(playerHands[0]),
            river0Snapshot = new List<TileType>(playerRivers[0]),
            river1Snapshot = new List<TileType>(playerRivers[1]),
            river2Snapshot = new List<TileType>(playerRivers[2]),
            doraSnapshot = GetVisibleDoraIndicators(),
            scoresSnapshot = (int[])playerScores.Clone()
        });
    }
    // ==========================================
    // 💡 AI振り返り画面の表示・切り替え処理
    // ==========================================
    public void OpenReview(int rankIndex)
    {
        Debug.Log($"👉 Top {rankIndex + 1} のボタンが押されました！");
        if (matchReviewList == null || rankIndex >= matchReviewList.Count)
        {
            Debug.Log($"⚠️ 表示できるデータがありません！（記録されたAIとのズレは全部で {matchReviewList?.Count} 個でした）");
            return;
        }

        ReviewData data = matchReviewList[rankIndex];
        if (reviewPanel != null) reviewPanel.SetActive(true);
        if (resultPanel != null) resultPanel.SetActive(false);

        // タイトルの更新
        if (reviewTitleText != null) {
            string pRiichi = data.isPlayerRiichi ? " <color=#FF8888>(リーチ宣言)</color>" : "";
            reviewTitleText.text = $"【Top {rankIndex + 1}】\n {data.kyokuName} {data.turn}巡目{pRiichi}";
        }

        // 当時の盤面をパネルに再現する（サイズを直接指定！）
        RenderSnapshotToPanel(data.handSnapshot, reviewHandPanel, new Vector2(50, 63));  // 手牌は大きく
        RenderSnapshotToPanel(data.river0Snapshot, reviewRiverPlayer, new Vector2(35, 47)); // 河は小さく
        RenderSnapshotToPanel(data.river1Snapshot, reviewRiverCpu1, new Vector2(35, 47));
        RenderSnapshotToPanel(data.river2Snapshot, reviewRiverCpu2, new Vector2(35, 47));
        RenderSnapshotToPanel(data.doraSnapshot, reviewDoraPanel, new Vector2(50, 65));    // ドラも手牌と同じく大きく
        
        // テキストを更新する前に、受け入れ枚数を先に計算しておく！
        // テキストを更新する前に、受け入れ枚数とシャンテン数を計算しておく！
        var aiUkeire = MahjongUtility.GetUkeireInfo(data.handSnapshot, data.aiDiscard);
        var playerUkeire = MahjongUtility.GetUkeireInfo(data.handSnapshot, data.playerDiscard);

        // 💡 追加：それぞれのシャンテン数の文字列を作成
        string aiShantenText = (aiUkeire.shanten == 0) ? "テンパイ" : $"{aiUkeire.shanten}向聴";
        string playerShantenText = (playerUkeire.shanten == 0) ? "テンパイ" : $"{playerUkeire.shanten}向聴";

        // あなたの打牌
        if (reviewPlayerDiscardImage != null) reviewPlayerDiscardImage.sprite = tileSprites[(int)data.playerDiscard];
        // 💡 修正：確率の下に「シャンテン数 / 受入枚数」を表示
        if (reviewPlayerProbText != null) reviewPlayerProbText.text = $"<size=36>あなた</size>\n<size=36>{data.playerProb:F1}%</size>\n<size=24><color=#FFFF00>{playerShantenText} / 受入: {playerUkeire.totalCount}枚</color></size>";

        // AIが推奨した牌と確率
        if (reviewAiDiscardImage != null) reviewAiDiscardImage.sprite = tileSprites[(int)data.aiDiscard];
        // 💡 修正：確率の下に「シャンテン数 / 受入枚数」を表示
        if (reviewAiProbText != null) reviewAiProbText.text = $"<size=36>AI推奨</size>\n<size=36>{data.aiProb:F1}%</size>\n<size=24><color=#FFFF00>{aiShantenText} / 受入: {aiUkeire.totalCount}枚</color></size>";
        for (int i = 0; i < 3; i++) {
            if (reviewTopButtons.Length > i && reviewTopButtons[i] != null) {
                reviewTopButtons[i].interactable = (i != rankIndex && i < matchReviewList.Count);
            }
        }

        string aiUkeireStr = MahjongUtility.CreateUkeirePromptText(aiUkeire);
        string playerUkeireStr = MahjongUtility.CreateUkeirePromptText(playerUkeire);

        // ボタンを連打された時用に前の通信を止めて、新しい解説をリクエスト
        StopCoroutine("RequestGasExplanation");
        StartCoroutine(RequestGasExplanation(data, aiUkeireStr, playerUkeireStr));
    }

    // 振り返り画面を閉じる
    public void CloseReview()
    {
        if (reviewPanel != null) reviewPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(true); // 閉じたらリザルト画面を再表示する

        // 閉じた時にスクロール枠を消す
        if (explanationScrollView != null) explanationScrollView.SetActive(false);
    }

    // スナップショット（リスト）から牌の画像を並べる専用関数
    // 💡 修正：サイズ(Vector2)を受け取るように変更した専用関数
    void RenderSnapshotToPanel(List<TileType> snapshot, Transform panel, Vector2 tileSize)
    {
        if (panel == null || snapshot == null) return;
        foreach (Transform child in panel) Destroy(child.gameObject); // 一度空にする

        foreach (TileType tile in snapshot) {
            GameObject newTile = new GameObject("SnapshotTile", typeof(RectTransform), typeof(Image));
            newTile.transform.SetParent(panel, false);
            
            Image img = newTile.GetComponent<Image>();
            img.sprite = tileSprites[(int)tile];

            // 指定されたサイズを適用する
            RectTransform rt = newTile.GetComponent<RectTransform>();
            rt.sizeDelta = tileSize; 
        }
    }

    // ==========================================
    // 💡 振り返り用：手牌の文字列化関数
    // ==========================================
    public string ConvertHandToPromptString(List<TileType> hand)
    {
        string manzu = ""; string pinzu = ""; string souzu = ""; string zihai = "";
        List<TileType> sortedHand = new List<TileType>(hand);
        SortHand(sortedHand);

        foreach (TileType tile in sortedHand) {
            int t = GetBaseTileId(tile); // 赤ドラも通常牌として処理
            if (t >= 0 && t <= 8) manzu += (t + 1).ToString();
            else if (t >= 9 && t <= 17) pinzu += (t - 9 + 1).ToString();
            else if (t >= 18 && t <= 26) souzu += (t - 18 + 1).ToString();
            else if (t >= 27 && t <= 33) {
                string[] zihaiNames = { "東", "南", "西", "北", "白", "發", "中" };
                zihai += zihaiNames[t - 27];
            }
        }
        string result = "";
        if (!string.IsNullOrEmpty(manzu)) result += manzu + "萬 ";
        if (!string.IsNullOrEmpty(pinzu)) result += pinzu + "筒 ";
        if (!string.IsNullOrEmpty(souzu)) result += souzu + "索 ";
        if (!string.IsNullOrEmpty(zihai)) result += zihai;
        return result.Trim();
    }
    private string GetScoreContext(int[] pastScores)
    {
        if (pastScores == null || pastScores.Length == 0) return "平場";

        int myScore = pastScores[0];
        int topScore = pastScores.Max();

        // 状況の条件分岐（数値はお好みで微調整してください）
        if (myScore == topScore && myScore >= 35000) 
            return "【トップ目】放銃リスクを極力避け、安全に局を消化したい場面。";
        
        if (topScore - myScore >= 15000) 
            return "【ラス目】多少の危険は承知で、打点（役の高さ）や逆転を狙いたい場面。";
        
        if (topScore - myScore <= 5000) 
            return "【接戦】速度重視。とにかく早くテンパイして先制リーチを打ちたい場面。";
        
        return "【平場】基本は牌効率に従いつつ、押し引きのバランスが重要な場面。";
    }

    // ==========================================
    // 💡 演出用メソッド群
    // ==========================================
    
    // 1. 指定した牌のImageをチカチカ黄色く光らせる
    private IEnumerator FlashTileEffect(Image targetImage)
    {
        highlightedTileImage = targetImage;
        Color originalColor = Color.white;
        while (true)
        {
            // 時間経過で 0.0 〜 1.0 を行き来する（PingPong）
            float pingPong = Mathf.PingPong(Time.time * 4f, 1f); 
            if (targetImage != null)
            {
                // 白と黄色を滑らかに行き来させる
                targetImage.color = Color.Lerp(originalColor, new Color(1f, 0.8f, 0.2f), pingPong);
            }
            yield return null;
        }
    }

    // 2. 光る演出を強制ストップする
    private void StopTileHighlight()
    {
        if (currentHighlightCoroutine != null)
        {
            StopCoroutine(currentHighlightCoroutine);
            currentHighlightCoroutine = null;
        }
        if (highlightedTileImage != null)
        {
            highlightedTileImage.color = Color.white; // 元の色に戻す
            highlightedTileImage = null;
        }
    }

    // 3. 一番右にある「牌の画像」だけを安全に取得する（Spacer対策）
    private Image GetLastTileImage(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Image img = parent.GetChild(i).GetComponent<Image>();
            if (img != null) return img;
        }
        return null;
    }

    // 💡 リーチの文字を表示して少し待つ演出
    private IEnumerator RiichiPresentationCoroutine()
    {
        if (agariText != null)
        {
            agariText.text = "リーチ！";
            agariText.gameObject.SetActive(true);
            yield return new WaitForSeconds(1.0f); // 1秒間表示
            agariText.gameObject.SetActive(false);
        }
    }

    // 4. ツモ/ロンの文字表示＆待機をしてからリザルト画面へ行く
    // 4. ツモ/ロンの文字表示＆待機をしてからリザルト画面へ行く
    private IEnumerator PlayAgariEffectAndShowResult(string text, Image targetTile, int winnerIndex, int[] scoreDiffs, string yakuString, string rankName, int scoreTotal, bool isOyaRenchan, bool isRyukyoku, string extraText)
    {
        // 文字を表示
        if (agariText != null)
        {
            agariText.text = text;
            agariText.gameObject.SetActive(true);
        }

        // 牌を光らせる
        if (targetTile != null)
        {
            currentHighlightCoroutine = StartCoroutine(FlashTileEffect(targetTile));
        }

        // 💡 演出のために 1.5秒間 待つ！
        yield return new WaitForSeconds(1.5f);

        // 演出を終わらせる
        StopTileHighlight();
        if (agariText != null) agariText.gameObject.SetActive(false);

        // 本当のリザルト画面表示（元々の処理）を呼ぶ！
        StartCoroutine(ShowAgariResultSequence(winnerIndex, scoreDiffs, yakuString, rankName, scoreTotal, isOyaRenchan, isRyukyoku, extraText));
    }

    // ==========================================
    // 💡 ルール選択ボタン用のメソッド
    // ==========================================
    public void OnTonpuuSelected()
    {
        currentGameLength = GameLength.Tonpuu;
        if (ruleSelectionPanel != null) ruleSelectionPanel.SetActive(false);
        InitializeGame(); // 💡 ここで初めてゲームスタート！
    }

    public void OnHanchanSelected()
    {
        currentGameLength = GameLength.Hanchan;
        if (ruleSelectionPanel != null) ruleSelectionPanel.SetActive(false);
        InitializeGame();
    }

//     // ==========================================
//     // 💡 GAS（Gemini API中継サーバー）通信ロジック
//     // ==========================================
//     [System.Serializable]
//     public class GasRequest {
//         public string prompt;
//     }

//     [System.Serializable]
//     public class GasResponse {
//         public string result;
//     }

//     private IEnumerator RequestGeminiExplanation(ReviewData data, string ukeireAi, string ukeirePlayer)
//     {
//         if (explanationScrollView != null) explanationScrollView.SetActive(true);
//         explanationText.text = "<color=#F1C40F>🤖 AI講師が当時のログを分析中...</color>";

//         string handStr = ConvertHandToPromptString(data.handSnapshot);
//         string aiTileName = tileNames[(int)data.aiDiscard];
//         string playerTileName = tileNames[(int)data.playerDiscard];
//         string scoreContext = GetScoreContext(data.scoresSnapshot);
//         string situationStr = $"{data.kyokuName} {data.turn}巡目 / {scoreContext}";

//         string promptMsg = $@"あなたは三人麻雀（三麻）専門のプロ講師です。
// 以下の【材料】を基に、なぜプレイヤーの打牌ではなくAI推奨の打牌が最善か、比較解説してください。

// 【材料】
// ・状況: {situationStr}
// ・当時の手牌: {handStr}
// ・AI推奨: {aiTileName} (推奨度: {data.aiProb:F1}%) → {aiTileName}を{ukeireAi}
// ・プレイヤー: {playerTileName} (推奨度: {data.playerProb:F1}%) → {playerTileName}を{ukeirePlayer}

// 【解説の3要素（優先順位順）】
// 1. 牌効率：受け入れ枚数やシャンテン数の具体的な変化を数値で示す。
// 2. 安全度：三麻では萬子(1,9のみ)や字牌は比較的安全。危険な無筋やドラに触れる。
// 3. 状況判断：現在の状況（{situationStr}）に基づいたスタンスを補足。

// 【出力条件】
// ・上記3要素を組み合わせて、3行程度で簡潔に断言すること。
// ・挨拶と「あなたの手牌は～」は一切不要。解説文のみ出力。";

//         // GASに送るシンプルなJSONデータを作成
//         GasRequest reqData = new GasRequest { prompt = promptMsg };
//         string json = JsonUtility.ToJson(reqData);

//         // ⚠️ ステップ2でコピーした「ウェブアプリのURL」をここに貼り付けます
//         string gasUrl = "https://script.google.com/macros/s/AKfycbyJWSc2cUmM-hMfGTDQ1c6xEZMyN-0zVx8MAkErZjF4zkbXNM4eDw4E0B5P4qsheJX7_g/exec";

//         using (UnityWebRequest request = new UnityWebRequest(gasUrl, "POST"))
//         {
//             byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
//             request.uploadHandler = new UploadHandlerRaw(bodyRaw);
//             request.downloadHandler = new DownloadHandlerBuffer();
//             request.SetRequestHeader("Content-Type", "application/json");

//             yield return request.SendWebRequest();

//             // 💡 ① 詳細なエラーメッセージはUnityコンソールに飛ばす（ここでコピペできます！）
//             Debug.LogError($"API通信エラー:\nHTTP Status: {request.responseCode}\nError: {request.error}\nDetails: {request.downloadHandler.text}");
            
//             // 💡 ② 画面（UI）にはシンプルなメッセージだけを表示する
//             explanationText.text = "<color=#FF5555>⚠️ エラーが発生しました。</color>";

//             if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
//             {
//                 Debug.LogError("GAS通信エラー: " + request.error);
//                 explanationText.text = "<color=#FF5555>⚠️ AIサーバーとの通信に失敗しました。</color>";
//             }
//             else
//             {
//                 // GASから返ってきたJSONをパースして表示
//                 GasResponse resData = JsonUtility.FromJson<GasResponse>(request.downloadHandler.text);
//                 // 💡 GASからの返答にエラーが含まれているかチェック
//                 if (resData.result.Contains("error") || resData.result.Contains("エラー") || resData.result.Contains("429"))
//                 {
//                     // コンソールに詳細を出す
//                     Debug.LogError($"GAS/Gemini エラー詳細:\n{resData.result}");
//                     // UIはシンプルに
//                     explanationText.text = "<color=#FF5555>⚠️ エラーが発生しました。</color>";
//                 }
//                 else
//                 {
//                     // 正常な解説の表示
//                     explanationText.text = $"<color=#3498DB>💡 講師の解説：</color>\n{resData.result}";
//                 }
//                 explanationText.text = $"<color=#3498DB>💡 講師の解説：</color>\n{resData.result}";
//             }
//         }
//     }

    // ==========================================
    // 💡 Groq (Llama 3) API通信ロジック
    // ==========================================
    
    // JSONパース用のデータ構造（OpenAI互換）
    // [System.Serializable]
    // public class GroqRequestMessage {
    //     public string role;
    //     public string content;
    // }
    // [System.Serializable]
    // public class GroqRequest {
    //     public string model;
    //     public GroqRequestMessage[] messages;
    //     public float temperature;
    // }
    // [System.Serializable]
    // public class GroqResponseChoice {
    //     public GroqRequestMessage message;
    // }
    // [System.Serializable]
    // public class GroqResponse {
    //     public GroqResponseChoice[] choices;
    // }

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

    // 💡 GameManager 用の通信メソッド（モード："review"）
    // ==========================================
    // 💡 初回のAI解説リクエスト（振り返り画面用）
    // ==========================================
    private IEnumerator RequestGasExplanation(ReviewData data, string ukeireAi, string ukeirePlayer)
    {
        if (explanationScrollView != null) explanationScrollView.SetActive(true);
        explanationText.text = "<color=#F1C40F>🤖 AI講師が当時のログを分析中...</color>";

        string handStr = ConvertHandToPromptString(data.handSnapshot);
        string aiTileName = tileNames[(int)data.aiDiscard];
        string playerTileName = tileNames[(int)data.playerDiscard];
        string scoreContext = GetScoreContext(data.scoresSnapshot);
        string situationStr = $"{data.kyokuName} {data.turn}巡目 / {scoreContext}";

        GasRequest reqData = new GasRequest {
            token = "mahjong_secret_2026",
            mode = "review",               
            situation = situationStr,
            hand = handStr,
            aiTile = aiTileName,
            aiProb = data.aiProb,
            aiUkeire = ukeireAi,
            playerTile = playerTileName,
            playerProb = data.playerProb,
            playerUkeire = ukeirePlayer,
            history = new List<ChatMessage>() // 初回は履歴なし
        };

        string jsonPayload = JsonUtility.ToJson(reqData);
        
        // ⚠️ ここにご自身の新しいGASのURLを貼り付けてください！
        string gasUrl = "https://script.google.com/macros/s/AKfycbwYp9Sdvaae-wN1QHXWpt6KYjhGrW7quo2xXNStZzu_o97xTblhtOOEVask67ubma4BOg/exec"; 

        using (UnityWebRequest request = new UnityWebRequest(gasUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "text/plain"); // CORS対策

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"GAS通信エラー: {request.error}");
                explanationText.text = "<color=#FF5555>⚠️ サーバーとの通信に失敗しました。</color>";
            }
            else
            {
                GasResponse resData = JsonUtility.FromJson<GasResponse>(request.downloadHandler.text);
                
                if (resData.result.Contains("error") || resData.result.Contains("不正なアクセス"))
                {
                    explanationText.text = "<color=#FF5555>⚠️ サーバーでエラーが発生しました。</color>";
                }
                else
                {
                    // 💡 スコープエラー解消：ここで履歴に追加してUIを更新！
                    explanationText.text = $"<color=#3498DB>💡 講師の解説：</color>\n{resData.result}";
                    
                    currentChatHistory.Clear();
                    currentChatHistory.Add(new ChatMessage { role = "assistant", content = resData.result });
                }
            }
        }
    }

    public void OnChatSubmitClicked()
    {
        if (chatInputField == null) return;

        // 1. テキストボックスの中身を取得する
        string userText = chatInputField.text;

        // 2. 空っぽなら何もしない
        if (string.IsNullOrWhiteSpace(userText)) return;

        // 3. テキストボックスの中身を空（リセット）にする
        chatInputField.text = "";

        // 4. 前回作った送信処理へテキストを渡す
        SendUserQuestion(userText);
    }

    // 💡 プレイヤーが質問を送信する時に呼ぶメソッド
    public void SendUserQuestion(string userText)
    {
        if (string.IsNullOrEmpty(userText)) return;

        // 1. 自分の発言を履歴に追加し、UIにも表示する
        currentChatHistory.Add(new ChatMessage { role = "user", content = userText });
        explanationText.text += $"\n\n<color=#00FF00>あなた：</color>\n{userText}";
        explanationText.text += "\n\n<color=#F1C40F>🤖 講師が考え中...</color>";

        // 2. GASに送信する
        StartCoroutine(RequestChatToGas());
    }

    private IEnumerator RequestChatToGas()
    {
        // 💡 モードを "chat" にして、会話履歴を丸ごと送る！
        GasRequest reqData = new GasRequest {
            token = "mahjong_secret_2026",
            mode = "chat",
            situation = "当時の状況（必要なら変数をいれる）",
            hand = "当時の手牌",
            history = currentChatHistory // 👈 ここが一番重要！
        };

        string jsonPayload = JsonUtility.ToJson(reqData);
        string gasUrl = "https://script.google.com/macros/s/AKfycbwYp9Sdvaae-wN1QHXWpt6KYjhGrW7quo2xXNStZzu_o97xTblhtOOEVask67ubma4BOg/exec";

        using (UnityWebRequest request = new UnityWebRequest(gasUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "text/plain");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                GasResponse resData = JsonUtility.FromJson<GasResponse>(request.downloadHandler.text);
                
                // 3. AIの返答を履歴に追加し、UIを更新する
                currentChatHistory.Add(new ChatMessage { role = "assistant", content = resData.result });
                
                // 「🤖 講師が考え中...」の文字を消して、最新の履歴からテキストを再構築する
                UpdateChatUI();
            }
        }
    }

    // 💡 UIのテキストを履歴から作り直す関数
    private void UpdateChatUI()
    {
        explanationText.text = "";
        foreach (var msg in currentChatHistory) {
            if (msg.role == "assistant") {
                explanationText.text += $"<color=#3498DB>💡 講師：</color>\n{msg.content}\n\n";
            } else if (msg.role == "user") {
                explanationText.text += $"<color=#00FF00>あなた：</color>\n{msg.content}\n\n";
            }
        }
    }
}