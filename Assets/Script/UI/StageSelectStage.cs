using System;
using UnityEngine;

/// <summary>One cartridge. A missing/disabled destination is always preview-only.</summary>
[CreateAssetMenu(menuName = "鉄球少女/Stage Select/Cartridge", fileName = "StageCartridge")]
public sealed class StageSelectStage : ScriptableObject
{
    [Serializable]
    public sealed class BackgroundLayer
    {
        public Sprite sprite;
        public Color tint = Color.white;
        [Range(0f, .12f)] public float drift = .025f;
    }

    public string stageName;
    [Tooltip("任意。未設定時はStage Nameをドットフォントで表示")]
    public Sprite titleSprite;
    [Tooltip("任意。カセットの外形を差し替え。ラベルにはステージ名だけを表示")]
    public Sprite cartridgeSprite;
    public Color cartridgeTint = Color.white;
    public Color accent = new Color(.5f, .85f, 1f);
    public string sceneName;
    public bool playable;
    [Tooltip("空欄なら最初から選択可能。指定Sceneのゴール結晶破壊で解放。収集アイテム数とは無関係")]
    public string unlockAfterScene;
    public string lockedMessage = "Stage1クリアで解放";
    public string unavailableMessage = "ただいま制作中";
    public Color screenColor = new Color(.1f, .15f, .25f);
    public BackgroundLayer far = new BackgroundLayer();
    public BackgroundLayer middle = new BackgroundLayer();
    public BackgroundLayer near = new BackgroundLayer();

    public bool IsUnlocked => StageSelectProgress.IsCleared(unlockAfterScene);
    public bool NeedsReveal => !string.IsNullOrEmpty(unlockAfterScene) && IsUnlocked && !StageSelectProgress.RevealCompleted(sceneName);
    public bool CanLaunch => IsUnlocked && playable && !string.IsNullOrWhiteSpace(sceneName)
        && Application.CanStreamedLevelBeLoaded(sceneName);
}
