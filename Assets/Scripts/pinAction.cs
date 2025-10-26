using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class PinIcon : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Popup Text")]
    public GameObject popupText;   // Assign your popup text GameObject here

    [Header("Animation")]
    public float zoomScale = 1.2f; // How much it zooms in
    public float zoomDuration = 0.1f; // How fast it zooms
    private Vector3 originalScale;

    [Header("Scene")]
    public string sceneToLoad; // Name of scene to load on click

    private void Start()
    {
        // Save the original scale
        originalScale = transform.localScale;

        // Hide popup text at start
        if (popupText != null)
            popupText.SetActive(false);
    }

    // When mouse/touch hovers
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (popupText != null)
            popupText.SetActive(true);
    }

    // When mouse/touch leaves
    public void OnPointerExit(PointerEventData eventData)
    {
        if (popupText != null)
            popupText.SetActive(false);
    }

    // When clicked
    public void OnPointerClick(PointerEventData eventData)
    {
        // Zoom animation
        StartCoroutine(ZoomAndLoadScene());
    }

    private System.Collections.IEnumerator ZoomAndLoadScene()
    {
        // Scale up
        float elapsed = 0f;
        while (elapsed < zoomDuration)
        {
            transform.localScale = Vector3.Lerp(originalScale, originalScale * zoomScale, elapsed / zoomDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.localScale = originalScale * zoomScale;

        // Small delay
        yield return new WaitForSeconds(0.1f);

        // Reset scale
        transform.localScale = originalScale;

        // Load new scene
        if (!string.IsNullOrEmpty(sceneToLoad))
        {
            SceneManager.LoadScene(sceneToLoad);
        }
    }
}
