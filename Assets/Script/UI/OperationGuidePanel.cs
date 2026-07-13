using UnityEngine;

public class OperationGuidePanel : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private GameObject operationGuidePanel;

    private void Awake()
    {
        if (operationGuidePanel != null)
        {
            operationGuidePanel.SetActive(false);
        }

        if (startPanel != null)
        {
            startPanel.SetActive(true);
        }
    }

    public void OpenGuide()
    {
        if (startPanel != null)
        {
            startPanel.SetActive(false);
        }

        if (operationGuidePanel != null)
        {
            operationGuidePanel.SetActive(true);
        }
    }

    public void CloseGuide()
    {
        if (operationGuidePanel != null)
        {
            operationGuidePanel.SetActive(false);
        }

        if (startPanel != null)
        {
            startPanel.SetActive(true);
        }
    }
}