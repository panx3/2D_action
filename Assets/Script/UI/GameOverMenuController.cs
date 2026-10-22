using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>HP0後の選択と時間停止を管理し、表示は既存のCRTオーバーレイへ委譲する。</summary>
[DisallowMultipleComponent]
public sealed class GameOverMenuController : MonoBehaviour
{
    [Header("CRT UI")]
    [SerializeField] private CRTOverlayController overlay;
    [SerializeField] private UnityEngine.UI.Button retryButton;
    [SerializeField] private UnityEngine.UI.Button titleButton;
    [Header("Scene Flow")]
    [SerializeField] private string titleSceneName = "TitleScene";

    private PauseMenuController pauseMenu;
    private GoalMenuController stageTimer;
    private Player player;
    private GameBgmController bgm;
    private bool transitioning;
    public bool IsGameOver { get; private set; }

    public static void ShowForDeath()
    {
        var menu = FindAnyObjectByType<GameOverMenuController>();
        if (menu == null)
        {
            var prefab = Resources.Load<GameOverMenuController>("UI/GameOverMenu");
            if (prefab == null)
            {
                Debug.LogError("[GameOver] UI/GameOverMenu prefab is missing.");
                return;
            }
            menu = Instantiate(prefab);
            menu.name = "GameOverMenu";
        }
        menu.ShowGameOver();
    }

    private void Awake()
    {
        retryButton.onClick.AddListener(RetryStage);
        titleButton.onClick.AddListener(ReturnToTitle);
        CRTMenuNavigation.Configure(retryButton, titleButton);
        pauseMenu = FindAnyObjectByType<PauseMenuController>();
        stageTimer = FindAnyObjectByType<GoalMenuController>();
        player = FindAnyObjectByType<Player>();
    }

    public void ShowGameOver()
    {
        if (IsGameOver || transitioning) return;
        if (pauseMenu != null) pauseMenu.SetExternalPauseBlocked(true);
        overlay.SetMessage("GAME OVER", "TRY AGAIN");
        if (!overlay.Show())
        {
            if (pauseMenu != null) pauseMenu.SetExternalPauseBlocked(false);
            Debug.LogError("[GameOver] CRT presentation could not be opened.", this);
            return;
        }
        IsGameOver = true;
        if (player != null)
        {
            player.SetInputEnabled(false);
            player.SetGoalInputBlocked(true);
        }
        if (stageTimer != null) stageTimer.StopTimerForGameOver();
        bgm = GameBgmController.Instance;
        if (bgm != null) bgm.SetPauseMuffled(true);
        Time.timeScale = 0f;
        StartCoroutine(SelectWhenReady());
    }

    private IEnumerator SelectWhenReady()
    {
        while (!overlay.IsReady) yield return null;
        EventSystem.current?.SetSelectedGameObject(retryButton.gameObject);
    }

    public void RetryStage() => LoadScene(SceneManager.GetActiveScene().path);
    public void ReturnToTitle() => LoadScene(titleSceneName);

    private void LoadScene(string scene)
    {
        if (!IsGameOver || !overlay.IsReady || transitioning || !Application.CanStreamedLevelBeLoaded(scene)) return;
        transitioning = true;
        overlay.Hide();
        CRTMenuNavigation.ClearSelection();
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
    }

    private void OnDisable()
    {
        if (!IsGameOver) return;
        overlay.Hide();
        CRTMenuNavigation.ClearSelection();
        if (player != null) player.SetGoalInputBlocked(false);
        if (bgm != null) bgm.SetPauseMuffled(false);
        if (pauseMenu != null) pauseMenu.SetExternalPauseBlocked(false);
        Time.timeScale = 1f;
    }
}
