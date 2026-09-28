using UnityEngine;

public sealed class FpsCounter : MonoBehaviour
{
    [SerializeField]
    [Min(0.05f)]
    private float updateInterval = 0.25f;

    [SerializeField]
    private Vector2 screenPosition =
        new Vector2(10f, 10f);

    [SerializeField]
    private int fontSize = 24;

    private float _elapsedTime;
    private int _frameCount;

    private float _displayedFps;
    private float _displayedFrameTime;

    private GUIStyle _style;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.targetFrameRate = 144;
    }

    private void Update()
    {
        _elapsedTime +=
            Time.unscaledDeltaTime;

        _frameCount++;

        if (_elapsedTime < updateInterval)
            return;

        _displayedFps =
            _frameCount /
            _elapsedTime;

        _displayedFrameTime =
            _displayedFps > 0f
                ? 1000f / _displayedFps
                : 0f;

        _elapsedTime = 0f;
        _frameCount = 0;
    }

    private void OnGUI()
    {
        if (_style == null)
        {
            _style =
                new GUIStyle(GUI.skin.box)
                {
                    fontSize = fontSize,
                    alignment =
                        TextAnchor.MiddleLeft,
                    normal =
                    {
                        textColor = Color.white
                    }
                };
        }

        string text =
            $"{_displayedFps:0} FPS  " +
            $"{_displayedFrameTime:0.0} ms";

        var rectangle =
            new Rect(
                screenPosition.x,
                screenPosition.y,
                230f,
                45f);

        GUI.Box(
            rectangle,
            text,
            _style);
    }
}