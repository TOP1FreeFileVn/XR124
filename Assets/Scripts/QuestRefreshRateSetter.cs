using System.Collections;
using System.Linq;
using UnityEngine;

public class QuestRefreshRateSetter : MonoBehaviour
{
    [Header("Quest Display")]
    public float targetRefreshRate = 90f;
    public bool applyOnStart = true;
    public bool logAvailableRates = true;

    [Header("Retry")]
    public float retryDurationSeconds = 5f;
    public float retryIntervalSeconds = 0.5f;

    private Coroutine applyRoutine;

    private void Start()
    {
        if (applyOnStart)
        {
            ApplyRefreshRate();
        }
    }

    [ContextMenu("Apply Refresh Rate")]
    public void ApplyRefreshRate()
    {
        if (applyRoutine != null)
        {
            StopCoroutine(applyRoutine);
        }

        applyRoutine = StartCoroutine(ApplyRefreshRateRoutine());
    }

    private IEnumerator ApplyRefreshRateRoutine()
    {
        float endTime = Time.realtimeSinceStartup + Mathf.Max(0f, retryDurationSeconds);

        while (Time.realtimeSinceStartup <= endTime)
        {
            if (TryApplyRefreshRate())
            {
                applyRoutine = null;
                yield break;
            }

            yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, retryIntervalSeconds));
        }

        Debug.LogWarning($"[QuestRefreshRateSetter] Could not apply {targetRefreshRate:0.#}Hz. Current={GetCurrentRefreshRate():0.#}Hz", this);
        applyRoutine = null;
    }

    private bool TryApplyRefreshRate()
    {
        float[] availableRates = OVRManager.display.displayFrequenciesAvailable;

        if (logAvailableRates)
        {
            string rates = availableRates != null && availableRates.Length > 0
                ? string.Join(", ", availableRates.Select(rate => rate.ToString("0.#")))
                : "none";
            Debug.Log($"[QuestRefreshRateSetter] Current={GetCurrentRefreshRate():0.#}Hz Available=[{rates}] Target={targetRefreshRate:0.#}Hz", this);
        }

        if (availableRates == null || availableRates.Length == 0)
        {
            return false;
        }

        float selectedRate = availableRates
            .OrderBy(rate => Mathf.Abs(rate - targetRefreshRate))
            .First();

        OVRManager.display.displayFrequency = selectedRate;
        Debug.Log($"[QuestRefreshRateSetter] Requested {selectedRate:0.#}Hz", this);
        return true;
    }

    private float GetCurrentRefreshRate()
    {
        return OVRManager.display != null ? OVRManager.display.displayFrequency : 0f;
    }
}
