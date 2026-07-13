using UnityEngine;
using UnityEngine.SceneManagement;

public class GameFlowManager : MonoBehaviour
{
    public static GameFlowManager Instance { get; private set; }

    [Header("画面UI")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private GameObject clearPanel;

    private bool gameStarted;
    private bool stageCleared;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        Time.timeScale = 0f;

        if (startPanel != null)
        {
            startPanel.SetActive(true);
        }

        if (clearPanel != null)
        {
            clearPanel.SetActive(false);
        }
    }

    public void StartGame()
    {
        if (stageCleared)
        {
            return;
        }

        gameStarted = true;

        if (startPanel != null)
        {
            startPanel.SetActive(false);
        }

        Time.timeScale = 1f;
    }

    public void ShowClear()
    {
        if (!gameStarted || stageCleared)
        {
            return;
        }

        stageCleared = true;
        Time.timeScale = 0f;

        if (clearPanel != null)
        {
            clearPanel.SetActive(true);
        }
    }

    public void RetryStage()
    {
        Time.timeScale = 1f;

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void OnDestroy()
    {
        Time.timeScale = 1f;
    }
}