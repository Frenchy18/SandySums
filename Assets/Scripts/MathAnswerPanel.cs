using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public sealed class MathAnswerPanel : MonoBehaviour
{
    // NC.1.OA.6 and CCSS 1.OA.C.6 both practice addition and subtraction within 20.
    private const int MaxWhole = 20;
    private const int ShellsPerRow = 5;
    private const int VisibleAnswerDigits = 16;
    private const float IncorrectFeedbackSeconds = 2.5f;

    [SerializeField] private Text leftOperand;
    [SerializeField] private Text operation;
    [SerializeField] private Text rightOperand;
    [SerializeField] private Text answerLabel;
    [SerializeField] private Text message;
    [SerializeField] private Button keyTemplate;
    [SerializeField] private RectTransform keypad;
    [SerializeField] private RectTransform leftShellGroup;
    [SerializeField] private RectTransform rightShellGroup;
    [SerializeField] private Texture2D shellArt;
    [SerializeField, Min(0f)] private float hintDelay = 10f;

    private RawImage[] leftShells;
    private RawImage[] rightShells;
    private Button checkKey;
    private Action<bool> onAnswered;
    private string answer = string.Empty;
    private int left;
    private int right;
    private bool subtract;
    private int correctAnswer;
    private float hintAt;
    private float finishAt;
    private bool submitted;

    private void Awake()
    {
        for (int digit = 1; digit <= 9; digit++)
            AddDigitKey(digit);

        CreateKey("Erase", Erase);
        AddDigitKey(0);
        checkKey = CreateKey("Check", CheckAnswer);
        ColorBlock colors = checkKey.colors;
        colors.normalColor = new Color(0.43f, 0.83f, 0.73f);
        colors.highlightedColor = new Color(0.61f, 0.95f, 0.84f);
        colors.pressedColor = new Color(0.27f, 0.69f, 0.58f);
        colors.selectedColor = colors.normalColor;
        checkKey.colors = colors;
        checkKey.GetComponentInChildren<Text>(true).color = new Color(0.05f, 0.18f, 0.22f);

        leftShells = CreateShells(leftShellGroup);
        rightShells = CreateShells(rightShellGroup);
    }

    public void Ask(Action<bool> answered)
    {
        int whole = UnityEngine.Random.Range(0, MaxWhole + 1);
        right = UnityEngine.Random.Range(0, whole + 1);
        subtract = UnityEngine.Random.Range(0, 2) == 0;
        left = subtract ? whole : whole - right;
        correctAnswer = subtract ? whole - right : whole;

        gameObject.SetActive(true);
        onAnswered = answered;
        submitted = false;
        answer = string.Empty;
        hintAt = Time.unscaledTime + hintDelay;
        leftOperand.text = left.ToString(CultureInfo.InvariantCulture);
        operation.text = subtract ? "−" : "+";
        rightOperand.text = right.ToString(CultureInfo.InvariantCulture);
        message.text = string.Empty;
        leftShellGroup.gameObject.SetActive(false);
        rightShellGroup.gameObject.SetActive(false);
        keypad.gameObject.SetActive(true);
        ShowAnswer();
    }

    private void Update()
    {
        if (onAnswered == null)
            return;

        if (submitted)
        {
            if (Time.unscaledTime >= finishAt)
                Finish(false);
        }
        else if (!leftShellGroup.gameObject.activeSelf && Time.unscaledTime >= hintAt)
        {
            ShowShells(leftShells, left, subtract ? right : 0);
            ShowShells(rightShells, right, 0);
            leftShellGroup.gameObject.SetActive(true);
            rightShellGroup.gameObject.SetActive(true);
            message.text = subtract ? "Count the bright shells on the left." : "Count both groups of shells.";
        }
    }

    private void AddDigitKey(int digit)
    {
        string label = digit.ToString(CultureInfo.InvariantCulture);
        CreateKey(label, () =>
        {
            if (submitted)
                return;

            answer += label;
            ShowAnswer();
        });
    }

    private Button CreateKey(string label, UnityEngine.Events.UnityAction pressed)
    {
        Button key = Instantiate(keyTemplate, keypad);
        key.name = label;
        key.GetComponentInChildren<Text>(true).text = label;
        key.onClick.AddListener(pressed);
        key.gameObject.SetActive(true);
        return key;
    }

    private void Erase()
    {
        if (submitted || answer.Length == 0)
            return;

        answer = answer[..^1];
        ShowAnswer();
    }

    private void ShowAnswer()
    {
        checkKey.interactable = answer.Length > 0;
        answerLabel.text = answer.Length == 0 ? "Your answer" : answer.Length > VisibleAnswerDigits
            ? "…" + answer[^VisibleAnswerDigits..] : answer;
    }

    private void CheckAnswer()
    {
        if (submitted || answer.Length == 0 || onAnswered == null)
            return;

        submitted = true;
        if (int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value == correctAnswer)
        {
            Finish(true);
            return;
        }

        message.text = $"The answer is {correctAnswer}. No move this turn.";
        keypad.gameObject.SetActive(false);
        finishAt = Time.unscaledTime + IncorrectFeedbackSeconds;
    }

    private void Finish(bool correct)
    {
        Action<bool> callback = onAnswered;
        onAnswered = null;
        gameObject.SetActive(false);
        callback(correct);
    }

    private RawImage[] CreateShells(RectTransform parent)
    {
        var shells = new RawImage[MaxWhole];
        for (int i = 0; i < shells.Length; i++)
        {
            var shell = new GameObject("Sand dollar", typeof(RectTransform)).AddComponent<RawImage>();
            shell.transform.SetParent(parent, false);
            shell.texture = shellArt;
            shell.uvRect = new Rect(0.25f, 0.35f, 0.50f, 0.30f);
            shell.raycastTarget = false;
            shell.rectTransform.anchorMin = shell.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            shell.rectTransform.sizeDelta = new Vector2(22f, 21f);
            shell.gameObject.SetActive(false);
            shells[i] = shell;
        }
        return shells;
    }

    private static void ShowShells(RawImage[] shells, int count, int removed)
    {
        for (int i = 0; i < shells.Length; i++)
        {
            RawImage shell = shells[i];
            shell.gameObject.SetActive(i < count);
            if (i >= count)
                continue;

            int row = i / ShellsPerRow;
            int rowCount = Mathf.Min(ShellsPerRow, count - row * ShellsPerRow);
            float column = i % ShellsPerRow - (rowCount - 1) * 0.5f;
            shell.rectTransform.anchoredPosition = new Vector2(column * 25f, -11f - row * 23f);
            shell.color = new Color(1f, 1f, 1f, i < count - removed ? 1f : 0.3f);
        }
    }
}
