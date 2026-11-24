using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public sealed class BoardMatch : MonoBehaviour
{
    private enum SpaceColor { None, Blue, Cyan, Red, Tan }
    private enum MatchPhase { PlayerCount, PlayerColor, Playing, Finished }

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
    [SerializeField, Tooltip("Piece renderers in player order.")]
    private Renderer[] pieces;
    [SerializeField, Tooltip("Available piece colors, independent of the board colors.")]
    private PieceColor[] palette;
    [SerializeField, Tooltip("Spaces in play order, with Start first and the rainbow Finish last.")]
    private BoardSpace[] path;
    [SerializeField] private Text status;

    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private const float SharedSpaceOffset = 0.0075f;

    private readonly Dictionary<Transform, int> spaceIndices = new();
    private MaterialPropertyBlock tint;
    private int[] positions;
    private int[] chosenColors;
    private float[] pieceHeights;
    private int maxPlayerCount;
    private int playerCount;
    private int currentPlayer;
    private float computerDrawAt;
    private MatchPhase phase;

    private void Awake()
    {
        if (deck != null)
            deck.DrawingAllowed = false;

        if (deck == null || status == null || pieces == null || pieces.Length == 0 ||
            Array.Exists(pieces, piece => piece == null) || palette == null || palette.Length == 0 ||
            path == null || path.Length < 2)
        {
            Debug.LogError("Assign the deck, status, pieces, palette, and ordered board path.", this);
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
        if (phase == MatchPhase.Playing)
        {
            if (currentPlayer == 0)
                return;

            if (deck.IsHoldingCard)
                computerDrawAt = Time.time + computerTurnDelay;
            else if (Time.time >= computerDrawAt)
                deck.TryDrawAutomatically();

            return;
        }

        if (phase == MatchPhase.Finished)
            return;

        bool confirm = OVRInput.GetDown(OVRInput.RawButton.B, OVRInput.Controller.RTouch) ||
                       OVRInput.GetDown(OVRInput.RawButton.Y, OVRInput.Controller.LTouch);
        if (confirm)
        {
            if (phase == MatchPhase.PlayerCount)
            {
                phase = MatchPhase.PlayerColor;
                PreviewPlayerColor(0);
            }
            else
                StartMatch();
        }
        else if (OVRInput.GetDown(OVRInput.RawButton.A, OVRInput.Controller.RTouch) ||
                 OVRInput.GetDown(OVRInput.RawButton.X, OVRInput.Controller.LTouch))
        {
            if (phase == MatchPhase.PlayerCount)
            {
                playerCount = playerCount % maxPlayerCount + 1;
                ShowPlayerCount();
            }
            else
                PreviewPlayerColor((chosenColors[0] + 1) % palette.Length);
        }
    }

    private void ShowPlayerCount()
    {
        for (int i = 0; i < pieces.Length; i++)
            pieces[i].gameObject.SetActive(i < playerCount);

        string count = playerCount == 1 ? "1 player (solo)" : $"{playerCount} players";
        ShowStatus(count, "A / X  Change count     B / Y  Confirm");
    }

    private void PreviewPlayerColor(int colorIndex)
    {
        SetPieceColor(0, colorIndex);
        ShowStatus($"Your color   /   {ColorLabel(palette[colorIndex])}",
            "A / X  Next color     B / Y  Start");
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
        deck.DrawingAllowed = true;
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

        positions[currentPlayer] = destination;
        PlacePiece(currentPlayer, path[destination].marker);
        if (destination == path.Length - 1)
        {
            phase = MatchPhase.Finished;
            deck.DrawingAllowed = false;
            string winner = currentPlayer == 0 ? "You win" : $"Computer {currentPlayer + 1} wins";
            ShowStatus(winner, "Reached the sandcastle");
            return;
        }

        currentPlayer = (currentPlayer + 1) % playerCount;
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

    private void PlacePiece(int player, Transform marker)
    {
        Vector3 position = marker.position + Vector3.up * pieceHeights[player];
        if (playerCount > 1)
            position += transform.right * (player % 2 == 0 ? -SharedSpaceOffset : SharedSpaceOffset);
        if (playerCount > 2)
            position += transform.forward * (player < 2 ? -SharedSpaceOffset : SharedSpaceOffset);

        pieces[player].transform.position = position;
    }

    private void ShowTurn()
    {
        bool computerTurn = currentPlayer != 0;
        deck.IsComputerTurn = computerTurn;
        computerDrawAt = Time.time + computerTurnDelay;

        PieceColor color = palette[chosenColors[currentPlayer]];
        string player = computerTurn ? $"Computer {currentPlayer + 1}" : "Your turn";
        string instruction = computerTurn ? "Drawing automatically" : "Grip and pull the top card";
        ShowStatus($"{player}   /   {ColorLabel(color)}", instruction);
    }

    private void ShowStatus(string heading, string instruction) =>
        status.text = $"{heading}\n<size=22>{instruction}</size>";

    private static string ColorLabel(PieceColor color) =>
        $"<color=#{ColorUtility.ToHtmlStringRGB(color.tint)}>{color.name}</color>";
}
