using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Reveals a TMP text one character at a time, like it's being typed.
///
/// It sets the full text up front and then raises TMP's visible-character
/// count, so the layout never reflows mid-animation and rich-text tags
/// (&lt;i&gt;, &lt;b&gt;, colours) are not counted as characters or shown as markup.
/// </summary>
[DisallowMultipleComponent]
public class TypewriterText : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [Tooltip("Characters revealed per second.")]
    [SerializeField] private float charactersPerSecond = 28f;
    [Tooltip("Pause before typing starts.")]
    [SerializeField] private float startDelay = 0.15f;

    private Coroutine _routine;
    private Action _onComplete;

    private void Reset() => label = GetComponent<TMP_Text>();

    private void Awake()
    {
        if (label == null) label = GetComponent<TMP_Text>();
    }

    /// <summary>
    /// Set the text and type it out from the beginning.
    /// <paramref name="onComplete"/> runs once the last character is revealed
    /// (or immediately if the animation can't run / is skipped).
    /// </summary>
    public void Play(string text, Action onComplete = null)
    {
        if (label == null)
        {
            onComplete?.Invoke();
            return;
        }

        label.text = text;
        if (_routine != null) StopCoroutine(_routine);
        _onComplete = onComplete;

        if (!gameObject.activeInHierarchy)
        {
            label.maxVisibleCharacters = int.MaxValue; // can't animate while hidden
            Finish();
            return;
        }

        _routine = StartCoroutine(TypeRoutine());
    }

    /// <summary>Skip to the fully revealed text (e.g. if the player taps).</summary>
    public void Complete()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        if (label != null) label.maxVisibleCharacters = int.MaxValue;
        Finish();
    }

    // Fires the callback once, so skipping can't run it twice.
    private void Finish()
    {
        Action callback = _onComplete;
        _onComplete = null;
        callback?.Invoke();
    }

    private IEnumerator TypeRoutine()
    {
        label.maxVisibleCharacters = 0;
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        // ForceMeshUpdate so textInfo.characterCount reflects the new text.
        label.ForceMeshUpdate();
        int total = label.textInfo.characterCount;

        float shown = 0f;
        float perSecond = Mathf.Max(1f, charactersPerSecond);

        while (shown < total)
        {
            shown += perSecond * Time.unscaledDeltaTime;
            label.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(shown));
            yield return null;
        }

        label.maxVisibleCharacters = int.MaxValue;
        _routine = null;
        Finish();
    }
}
