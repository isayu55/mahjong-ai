using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    // 💡 AI推論シーンを読み込む
    public void GoToAITestMode()
    {
        SceneManager.LoadScene("AITestScene"); 
    }

    // 💡 対戦シーンを読み込む
    public void GoToGameMode()
    {
        SceneManager.LoadScene("BattleScene"); 
    }

    // 💡 追加：メニュー画面に戻る！
    public void GoToMenu()
    {
        SceneManager.LoadScene("MenuScene"); 
    }
    
    // ゲーム終了用
    public void QuitGame()
    {
        Debug.Log("ゲームを終了します");
        Application.Quit();
    }
}