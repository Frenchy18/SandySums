using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CardDeckSpawner : MonoBehaviour
{
    [Serializable]
    private sealed class SpecialCard
    {
        public Transform destination;
        public GameObject prefab;
    }

    public readonly struct Card
    {
        public readonly string Color;
        public readonly int Matches;
        public readonly Transform Destination;
        internal readonly GameObject Prefab;

        internal Card(GameObject prefab, string color, int matches, Transform destination = null)
        {
            Prefab = prefab;
            Color = color;
            Matches = matches;
            Destination = destination;
        }
    }

    [SerializeField, Min(0), Tooltip("Total color cards, before adding one card per special destination.")]
    private int coloredCardCount = 40;
    [SerializeField, Tooltip("Assign single cards in this order: Blue, Cyan, Red, Tan.")]
    private GameObject[] singleCards;
    [SerializeField, Tooltip("Assign double cards in this order: Blue, Cyan, Red, Tan.")]
    private GameObject[] doubleCards;
    [SerializeField] private SpecialCard[] specialCards;
    [SerializeField] private Transform leftController;
    [SerializeField] private Transform rightController;
    [SerializeField, Min(0.01f)] private float gripReach = 0.085f;
    [SerializeField, Min(0.01f)] private float pullDistance = 0.055f;

    public event Action<Card> CardDrawn;
    public int RemainingCards => deck.Count;

    private static readonly string[] Colors = { "Blue", "Cyan", "Red", "Tan" };
    private const int MaxVisibleCards = 12;

    private readonly List<Card> deck = new();
    private readonly Transform[] controllers = new Transform[2];
    private readonly bool[] gripping = new bool[2];
    private readonly bool[] pressed = new bool[2];
    private readonly Transform[] layers = new Transform[MaxVisibleCards];

    private Transform deckVisual;
    private GameObject pulledCard;
    private GameObject shownCard;
    private Pose cardGrip;
    private Vector3 pullStart;
    private Vector3 cardSize;
    private int pullHand = -1;
    private bool revealed;

    private void Start()
    {
        controllers[0] = leftController;
        controllers[1] = rightController;
        if (leftController == null || rightController == null ||
            singleCards == null || doubleCards == null ||
            singleCards.Length != Colors.Length || doubleCards.Length != Colors.Length ||
            Array.Exists(singleCards, card => card == null) ||
            Array.Exists(doubleCards, card => card == null))
        {
            Debug.LogError("Assign both controller anchors and the four single and double card prefabs.", this);
            enabled = false;
            return;
        }

        GameObject sample = singleCards[0];
        cardSize = sample.GetComponent<BoxCollider>().size;
        var body = sample.transform.Find("Body");
        deckVisual = new GameObject("Deck").transform;
        deckVisual.SetParent(transform, false);

        for (int i = 0; i < layers.Length; i++)
        {
            var layer = new GameObject("Face-down card");
            layer.transform.SetParent(deckVisual, false);
            layer.transform.localScale = cardSize;
            layer.AddComponent<MeshFilter>().sharedMesh = body.GetComponent<MeshFilter>().sharedMesh;
            var renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = body.GetComponent<MeshRenderer>().sharedMaterial;
            var colors = new MaterialPropertyBlock();
            colors.SetColor("_BaseColor", i % 2 == 0 ? new Color(0.80f, 0.90f, 0.91f) : Color.white);
            renderer.SetPropertyBlock(colors);
            layers[i] = layer.transform;
        }

        RefillDeck();
    }

    private void LateUpdate()
    {
        ReadGrip(0, OVRInput.Controller.LTouch);
        ReadGrip(1, OVRInput.Controller.RTouch);

        if (pullHand >= 0)
        {
            if (gripping[pullHand])
            {
                Follow(pulledCard.transform, controllers[pullHand], cardGrip);
                float drag = Vector3.Distance(pullStart, deckVisual.InverseTransformPoint(pulledCard.transform.position));
                if (!revealed && drag >= pullDistance)
                    RevealCard();
            }
            else
                ReleaseCard();

            return;
        }

        if (deck.Count == 0)
            return;

        for (int hand = 0; hand < controllers.Length; hand++)
        {
            if (!pressed[hand] || Vector3.Distance(controllers[hand].position, TopOfDeck().position) > gripReach)
                continue;

            BeginPull(hand);
            break;
        }
    }

    private void ReadGrip(int hand, OVRInput.Controller controller)
    {
        bool wasGripping = gripping[hand];
        gripping[hand] = OVRInput.GetControllerPositionTracked(controller) &&
                         OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, controller);
        pressed[hand] = gripping[hand] && !wasGripping;
    }

    private void RefillDeck()
    {
        var variants = new List<Card>(Colors.Length * 2);
        for (int i = 0; i < Colors.Length; i++)
        {
            variants.Add(new Card(singleCards[i], Colors[i], 1));
            variants.Add(new Card(doubleCards[i], Colors[i], 2));
        }

        // Randomize which variants receive any remainder when the size is not a multiple of eight.
        Shuffle(variants);
        for (int i = 0; i < coloredCardCount; i++)
            deck.Add(variants[i % variants.Count]);

        var destinations = new HashSet<Transform>();
        foreach (SpecialCard special in specialCards ?? Array.Empty<SpecialCard>())
        {
            if (special != null && special.destination != null && destinations.Add(special.destination))
                deck.Add(new Card(special.prefab, null, 0, special.destination));
        }

        Shuffle(deck);
        UpdateStack();
    }

    private static void Shuffle(List<Card> cards)
    {
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int swap = UnityEngine.Random.Range(0, i + 1);
            (cards[i], cards[swap]) = (cards[swap], cards[i]);
        }
    }

    private void UpdateStack()
    {
        int visible = Mathf.Min(deck.Count, layers.Length);
        for (int i = 0; i < layers.Length; i++)
        {
            layers[i].gameObject.SetActive(i < visible);
            layers[i].localPosition = Vector3.up * (i * cardSize.y * 1.06f);
        }
    }

    private Transform TopOfDeck() => layers[Mathf.Min(deck.Count, layers.Length) - 1];

    private void BeginPull(int hand)
    {
        Transform top = TopOfDeck();
        pulledCard = Instantiate(singleCards[0], top.position, top.rotation, transform);
        pulledCard.GetComponent<BoxCollider>().enabled = false;
        pulledCard.transform.Find("Face").gameObject.SetActive(false);
        top.gameObject.SetActive(false);
        pullHand = hand;
        pullStart = deckVisual.InverseTransformPoint(pulledCard.transform.position);
        cardGrip = RelativePose(controllers[hand], pulledCard.transform);
    }

    private void CreateSpecialLabel(Transform card, string destinationName)
    {
        int start = destinationName.IndexOf('(');
        string label = start >= 0 ? destinationName.Substring(start + 1).TrimEnd(')') : destinationName;
        var labelObject = new GameObject("Special card label");
        labelObject.transform.SetParent(card, false);
        labelObject.transform.localPosition = Vector3.up * (cardSize.y * 0.6f);
        labelObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var text = labelObject.AddComponent<TextMesh>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        labelObject.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
        text.text = label;
        text.anchor = TextAnchor.MiddleCenter;
        text.fontSize = 48;
        text.characterSize = 0.006f;
        text.color = new Color(0.05f, 0.22f, 0.28f);
    }

    private void RevealCard()
    {
        Card card = deck[^1];
        deck.RemoveAt(deck.Count - 1);
        revealed = true;
        if (card.Prefab != null)
        {
            GameObject finishedCard = Instantiate(card.Prefab, pulledCard.transform.position,
                pulledCard.transform.rotation, transform);
            foreach (Collider collider in finishedCard.GetComponentsInChildren<Collider>())
                collider.enabled = false;

            pulledCard.SetActive(false);
            Destroy(pulledCard);
            pulledCard = finishedCard;
        }
        else
            CreateSpecialLabel(pulledCard.transform, card.Destination.name);

        pulledCard.name = card.Destination != null ? card.Destination.name : card.Prefab.name;

        UpdateStack();
        CardDrawn?.Invoke(card);
    }

    private void ReleaseCard()
    {
        if (revealed)
        {
            if (shownCard != null)
                Destroy(shownCard);

            shownCard = pulledCard;
            shownCard.transform.SetLocalPositionAndRotation(Vector3.right * (cardSize.x + 0.015f), Quaternion.identity);
            if (deck.Count == 0)
                RefillDeck();
        }
        else
        {
            Destroy(pulledCard);
            UpdateStack();
        }

        pulledCard = null;
        pullHand = -1;
        revealed = false;
    }

    private static Pose RelativePose(Transform hand, Transform item) =>
        new Pose(hand.InverseTransformPoint(item.position), Quaternion.Inverse(hand.rotation) * item.rotation);

    private static void Follow(Transform item, Transform hand, Pose offset) =>
        item.SetPositionAndRotation(hand.TransformPoint(offset.position), hand.rotation * offset.rotation);
}
