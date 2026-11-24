using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

public sealed class BoardMatch : MonoBehaviour
{
    private enum SpaceColor { None, Blue, Cyan, Red, Tan }
    private enum MatchPhase { PlayerCount, PlayerColor, Playing, Moving, Finished, Leaving }

    [Serializable]
    private struct BoardSpace
    {
        public Transform marker;
        public SpaceColor color;
    }

    [Serializable]
    private struct PieceColor
    {
        public string name;
        public Color tint;
    }

    [SerializeField] private CardDeckSpawner deck;
    [SerializeField, Range(1, 4), FormerlySerializedAs("playerCount")]
    private int defaultPlayerCount = 4;
    [SerializeField, Min(0.1f)] private float computerTurnDelay = 1.25f;
    [SerializeField, Min(0.01f)] private float secondsPerHop = 0.14f;
    [SerializeField, Min(0.01f)] private float maxMoveSeconds = 5.25f;
    [SerializeField, Min(0f)] private float hopHeight = 0.016f;
    [SerializeField] private Material highlightMaterial;
    [SerializeField, Tooltip("Piece renderers in player order.")]
    private Renderer[] pieces;
    [SerializeField, Tooltip("Available piece colors, independent of the board colors.")]
    private PieceColor[] palette;
    [SerializeField, Tooltip("Spaces in play order, with Start first and the rainbow Finish last.")]
    private BoardSpace[] path;
    [SerializeField] private GameObject setupPanel;
    [SerializeField] private GameObject setupOptions;
    [SerializeField] private Text setupTitle;
    [SerializeField] private Text setupValue;
    [SerializeField] private Text setupConfirm;
    [SerializeField] private GameObject endOptions;
    [SerializeField] private Text winnerLabel;
    [SerializeField] private GameObject matchDisplay;
    [SerializeField] private Text status;

    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private const float SharedSpaceOffset = 0.0075f;
    private const float DestinationPreviewSeconds = 0.16f;

    private readonly Dictionary<Transform, int> spaceIndices = new();
    private MaterialPropertyBlock tint;
    private int[] positions;
    private int[] chosenColors;
    private float[] pieceHeights;
    private int maxPlayerCount;
    private int playerCount;
    private int currentPlayer;
    private float computerDrawAt;
    private int moveDestination;
    private int hopSpace;
    private int hopDirection;
    private float hopDuration;
    private float hopElapsed;
    private Vector3 hopStart;
    private LineRenderer activeHighlight;
    private LineRenderer destinationHighlight;
    private MatchPhase phase;

    private void Awake()
    {
        if (deck != null)
            deck.DrawingAllowed = false;

        if (deck == null || status == null || matchDisplay == null || setupPanel == null ||
            setupOptions == null || endOptions == null || winnerLabel == null ||
            setupTitle == null || setupValue == null || setupConfirm == null || highlightMaterial == null ||
            pieces == null || pieces.Length == 0 ||
            Array.Exists(pieces, piece => piece == null) || palette == null || palette.Length == 0 ||
            path == null || path.Length < 2)
        {
            Debug.LogError("Assign the deck, board path, pieces, palette, displays, and highlight material.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < path.Length; i++)
        {
            Transform marker = path[i].marker;
            if (marker != null && spaceIndices.TryAdd(marker, i))
                continue;

            Debug.LogError($"Board path entry {i} needs a unique marker.", this);
            enabled = false;
            return;
        }

        maxPlayerCount = Mathf.Min(4, Mathf.Min(pieces.Length, palette.Length));
        playerCount = Mathf.Clamp(defaultPlayerCount, 1, maxPlayerCount);
        positions = new int[maxPlayerCount];
        chosenColors = new int[maxPlayerCount];
        pieceHeights = new float[maxPlayerCount];
        tint = new MaterialPropertyBlock();
        activeHighlight = CreateHighlight("Active piece highlight", 0.012f, 0.0035f, new Color(1f, 0.72f, 0.16f));
        destinationHighlight = CreateHighlight("Destination highlight", 0.021f, 0.003f, new Color(0.02f, 0.26f, 0.37f));
        setupOptions.SetActive(true);
        endOptions.SetActive(false);
        setupPanel.SetActive(true);
        matchDisplay.SetActive(false);
        setupTitle.text = "Choose player count";
        setupConfirm.text = "Continue";
        ShowPlayerCount();
    }

    private void OnEnable()
    {
        if (positions == null)
            return;

        deck.CardDrawn += MoveCurrentPiece;
        deck.DrawingAllowed = phase == MatchPhase.Playing;
    }

    private void OnDisable()
    {
        if (deck == null)
            return;

        deck.CardDrawn -= MoveCurrentPiece;
        deck.DrawingAllowed = false;
    }

    private void Update()
    {
        if (phase == MatchPhase.Moving)
        {
            AdvanceHop(Time.deltaTime);
            return;
        }

        if (phase != MatchPhase.Playing || currentPlayer == 0)
            return;

        if (deck.IsHoldingCard)
            computerDrawAt = Time.time + computerTurnDelay;
        else if (Time.time >= computerDrawAt)
            deck.TryDrawAutomatically();
    }

    public void PreviousSetupOption() => ChangeSetupOption(-1);
    public void NextSetupOption() => ChangeSetupOption(1);

    public void ConfirmSetup()
    {
        if (!isActiveAndEnabled)
            return;

        if (phase == MatchPhase.PlayerCount)
        {
            phase = MatchPhase.PlayerColor;
            setupTitle.text = "Choose your color";
            setupConfirm.text = "Start Game";
            PreviewPlayerColor(0);
        }
        else if (phase == MatchPhase.PlayerColor)
            StartMatch();
    }

    public void PlayAgain()
    {
        if (!isActiveAndEnabled || phase != MatchPhase.Finished)
            return;

        phase = MatchPhase.Leaving;
        SceneManager.LoadScene(gameObject.scene.buildIndex);
    }

    public void ExitGame()
    {
        if (!isActiveAndEnabled || phase != MatchPhase.Finished)
            return;

        phase = MatchPhase.Leaving;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ChangeSetupOption(int direction)
    {
        if (!isActiveAndEnabled)
            return;

        if (phase == MatchPhase.PlayerCount)
        {
            playerCount = (playerCount - 1 + direction + maxPlayerCount) % maxPlayerCount + 1;
            ShowPlayerCount();
        }
        else if (phase == MatchPhase.PlayerColor)
            PreviewPlayerColor((chosenColors[0] + direction + palette.Length) % palette.Length);
    }

    private void ShowPlayerCount()
    {
        for (int i = 0; i < pieces.Length; i++)
            pieces[i].gameObject.SetActive(i < playerCount);

        setupValue.text = playerCount == 1 ? "1 player (solo)" : $"{playerCount} players";
    }

    private void PreviewPlayerColor(int colorIndex)
    {
        SetPieceColor(0, colorIndex);
        setupValue.text = ColorLabel(palette[colorIndex]);
    }

    private void SetPieceColor(int player, int colorIndex)
    {
        chosenColors[player] = colorIndex;
        Renderer piece = pieces[player];
        piece.GetPropertyBlock(tint);
        tint.SetColor(BaseColor, palette[colorIndex].tint);
        piece.SetPropertyBlock(tint);
    }

    private void StartMatch()
    {
        var availableColors = new List<int>(palette.Length - 1);
        for (int i = 0; i < palette.Length; i++)
        {
            if (i != chosenColors[0])
                availableColors.Add(i);
        }

        for (int i = 1; i < playerCount; i++)
        {
            int choice = UnityEngine.Random.Range(0, availableColors.Count);
            SetPieceColor(i, availableColors[choice]);
            availableColors.RemoveAt(choice);
        }

        for (int i = 0; i < playerCount; i++)
        {
            Renderer piece = pieces[i];
            pieceHeights[i] = piece.transform.position.y - piece.bounds.min.y;
        }

        phase = MatchPhase.Playing;
        setupPanel.SetActive(false);
        matchDisplay.SetActive(true);
        ShowTurn();
    }

    private void MoveCurrentPiece(CardDeckSpawner.Card card)
    {
        if (phase != MatchPhase.Playing)
            return;

        int destination;
        if (card.Destination != null)
        {
            if (!spaceIndices.TryGetValue(card.Destination, out destination))
            {
                Debug.LogError($"Special card destination {card.Destination.name} is missing from the board path.", this);
                return;
            }
        }
        else
        {
            if (!Enum.TryParse(card.Color, out SpaceColor color) || color == SpaceColor.None || card.Matches < 1)
                return;

            destination = FindColorDestination(positions[currentPlayer], color, card.Matches);
        }

        phase = MatchPhase.Moving;
        deck.DrawingAllowed = false;
        moveDestination = destination;
        hopSpace = positions[currentPlayer];
        hopDirection = Math.Sign(destination - hopSpace);
        int steps = Mathf.Max(1, Mathf.Abs(destination - hopSpace));
        hopDuration = Mathf.Max(0.001f, Mathf.Min(secondsPerHop, maxMoveSeconds / steps));
        hopElapsed = -DestinationPreviewSeconds;
        hopStart = pieces[currentPlayer].transform.position;

        destinationHighlight.transform.position = path[destination].marker.position + Vector3.up * 0.002f;
        destinationHighlight.gameObject.SetActive(true);
        ShowStatus(TurnLabel(), "Moving to highlighted space");
    }

    private void AdvanceHop(float deltaTime)
    {
        hopElapsed += deltaTime;
        if (hopElapsed < 0f)
            return;

        while (hopElapsed >= hopDuration)
        {
            hopElapsed -= hopDuration;
            hopSpace += hopDirection;
            hopStart = PiecePosition(currentPlayer, path[hopSpace].marker);
            SetMovingPiecePosition(hopStart);
            if (hopSpace == moveDestination)
            {
                CompleteMove();
                return;
            }
        }

        Vector3 nextPosition = PiecePosition(currentPlayer, path[hopSpace + hopDirection].marker);
        float progress = hopElapsed / hopDuration;
        Vector3 arc = Vector3.up * (hopHeight * Mathf.Sin(Mathf.PI * progress));
        SetMovingPiecePosition(Vector3.Lerp(hopStart, nextPosition, progress) + arc);
    }

    private void CompleteMove()
    {
        positions[currentPlayer] = moveDestination;
        destinationHighlight.gameObject.SetActive(false);
        if (moveDestination == path.Length - 1)
        {
            phase = MatchPhase.Finished;
            winnerLabel.text = currentPlayer == 0 ? "You win" : $"Computer {currentPlayer + 1} wins";
            setupTitle.text = "Play Again?";
            setupOptions.SetActive(false);
            endOptions.SetActive(true);
            matchDisplay.SetActive(false);
            setupPanel.SetActive(true);
            return;
        }

        currentPlayer = (currentPlayer + 1) % playerCount;
        phase = MatchPhase.Playing;
        ShowTurn();
    }

    private int FindColorDestination(int position, SpaceColor color, int matches)
    {
        int finish = path.Length - 1;
        int lastMatch = finish;
        for (int i = position + 1; i < finish; i++)
        {
            if (path[i].color != color)
                continue;

            lastMatch = i;
            if (--matches == 0)
                break;
        }

        // A double with one match left stops there. The castle is available with no matches ahead.
        return lastMatch;
    }

    private Vector3 PiecePosition(int player, Transform marker)
    {
        Vector3 position = marker.position + Vector3.up * pieceHeights[player];
        if (playerCount > 1)
            position += transform.right * (player % 2 == 0 ? -SharedSpaceOffset : SharedSpaceOffset);
        if (playerCount > 2)
            position += transform.forward * (player < 2 ? -SharedSpaceOffset : SharedSpaceOffset);

        return position;
    }

    private void SetMovingPiecePosition(Vector3 position)
    {
        pieces[currentPlayer].transform.position = position;
        activeHighlight.transform.position = position + Vector3.up * 0.002f;
    }

    private LineRenderer CreateHighlight(string name, float radius, float width, Color color)
    {
        var ring = new GameObject(name).AddComponent<LineRenderer>();
        ring.transform.SetParent(transform, false);
        ring.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.alignment = LineAlignment.TransformZ;
        ring.widthMultiplier = width;
        ring.sharedMaterial = highlightMaterial;
        ring.shadowCastingMode = ShadowCastingMode.Off;
        ring.receiveShadows = false;

        const int segments = 32;
        ring.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = 2f * Mathf.PI * i / segments;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }

        var properties = new MaterialPropertyBlock();
        properties.SetColor(BaseColor, color);
        ring.SetPropertyBlock(properties);
        ring.gameObject.SetActive(false);
        return ring;
    }

    private void ShowTurn()
    {
        bool computerTurn = currentPlayer != 0;
        deck.IsComputerTurn = computerTurn;
        deck.DrawingAllowed = true;
        computerDrawAt = Time.time + computerTurnDelay;
        activeHighlight.transform.position = pieces[currentPlayer].transform.position + Vector3.up * 0.002f;
        activeHighlight.gameObject.SetActive(true);

        string instruction = computerTurn ? "Drawing automatically" : "Grip and pull the top card";
        ShowStatus(TurnLabel(), instruction);
    }

    private string TurnLabel()
    {
        string player = currentPlayer == 0 ? "Your turn" : $"Computer {currentPlayer + 1}";
        return $"{player}   /   {ColorLabel(palette[chosenColors[currentPlayer]])}";
    }

    private void ShowStatus(string heading, string instruction) =>
        status.text = $"{heading}\n<size=22>{instruction}</size>";

    private static string ColorLabel(PieceColor color) =>
        $"<color=#{ColorUtility.ToHtmlStringRGB(color.tint)}>{color.name}</color>";
}
