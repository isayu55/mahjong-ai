using UnityEngine;
using UnityEngine.UI;
using System.Runtime.InteropServices;
using System;

public class ImageUploader : MonoBehaviour
{
    [Header("画像を表示する場所")]
    public RawImage previewImage;

    public YoloTileDetector yoloDetector;

    // JavaScriptの関数を呼び出すためのおまじない
    [DllImport("__Internal")]
    private static extern void OpenImagePicker();

    // ボタンが押された時に呼ばれる関数
    public void OnUploadButtonClicked()
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
            // WebGLビルドされた時だけ、ブラウザのカメラ起動機能を開く
            OpenImagePicker();
        #else
            Debug.Log("⚠️ エディタ上ではカメラは起動しません。WebGLビルド後に動作します！");
        #endif
    }

    // 💡 JavaScriptから Base64（文字列化された画像）を受け取る関数
    public void ReceiveImageBase64(string base64)
    {
        Debug.Log("ブラウザから画像を受け取りました！");

        // 文字列を画像データ(byte配列)に戻す
        byte[] imageBytes = Convert.FromBase64String(base64);
        
        // 空のテクスチャを作って、画像データを流し込む
        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(imageBytes);

        // 画面に表示する
        previewImage.texture = tex;
        previewImage.color = Color.white;
        
        // 縦横比を整える
        var fitter = previewImage.GetComponent<AspectRatioFitter>();
        if (fitter != null) {
            fitter.aspectRatio = (float)tex.width / tex.height;
        }

        // 👇 追加：画像をYOLOに渡して解析スタート！
        if (yoloDetector != null) {
            yoloDetector.DetectTiles(tex);
        }
    }
}