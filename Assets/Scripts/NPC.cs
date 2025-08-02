using UnityEngine;
using Oculus.Interaction;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public class NPC : MonoBehaviour
{
    [SerializeField]
    private MonoBehaviour interactableViewSource; // Assign your Interactable (e.g., RayInteractable) in the inspector
    WhisperManager whisperManager;
    [SerializeField] Transform player;
    Animator animator;
    private IInteractableView interactableView;

    [SerializeField] SkinnedMeshRenderer headMeshRenderer;

    // Talking animation variables
    private bool isTalking = false;
    private Coroutine talkingCoroutine;
    private const int PHONEME_START_INDEX = 35;
    private const int PHONEME_COUNT = 10; // Indices 35-44 (10 phonemes)
    private float[] currentPhonemeWeights;
    private float[] targetPhonemeWeights;

    public bool talkHo;

    [SerializeField] InputAction journalInputAction;
    [SerializeField] GameObject journalCanvas;

    private void Awake()
    {
        interactableView = interactableViewSource as IInteractableView;
        journalInputAction.performed += OnJournalInputActionPerformed;
    }

    void OnJournalInputActionPerformed(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            journalCanvas.SetActive(!journalCanvas.activeSelf);
        }
    }

    private void Start()
    {
        whisperManager = FindAnyObjectByType<WhisperManager>();
        animator = GetComponent<Animator>();

        // Initialize phoneme weight arrays
        currentPhonemeWeights = new float[PHONEME_COUNT];
        targetPhonemeWeights = new float[PHONEME_COUNT];
    }

    private void Update()
    {
        if (player != null)
        {
            Vector3 levelPlayerPosition = new Vector3(player.position.x, transform.position.y, player.position.z);
            Vector3 directionToPlayer = (levelPlayerPosition - transform.position).normalized;
            Vector3 forward = transform.forward;
            float angle = Vector3.Angle(forward, directionToPlayer);

            bool turningLeft = false;
            bool turningRight = false;

            if (angle > 15f)
            {
                float cross = Vector3.Cross(forward, directionToPlayer).y;
                if (cross > 0)
                {
                    turningRight = true;
                }
                else if (cross < 0)
                {
                    turningLeft = true;
                }
            }
            animator.SetBool("TurningRight", turningRight);
            animator.SetBool("TurningLeft", turningLeft);
        }

        if (talkHo)
        {
            StartTalking();
            talkHo = false;
        }
    }

    private void OnEnable()
    {
        if (interactableView != null)
        {
            interactableView.WhenStateChanged += OnStateChanged;
        }
        journalInputAction.Enable();
    }

    private void OnDisable()
    {
        if (interactableView != null)
        {
            interactableView.WhenStateChanged -= OnStateChanged;
        }
        journalInputAction.Disable();
    }

    private void OnStateChanged(InteractableStateChangeArgs args)
    {
        if (args.NewState == InteractableState.Select)
        {
            Debug.Log("Hovering");
            whisperManager.ToggleRecording();
        }
        else if (args.PreviousState == InteractableState.Select && args.NewState != InteractableState.Select)
        {
            Debug.Log("Unhovering");
            whisperManager.ToggleRecording();
        }
    }

    // Public API for talking animation
    public void StartTalking()
    {
        if (!isTalking && headMeshRenderer != null)
        {
            isTalking = true;
            talkingCoroutine = StartCoroutine(TalkingAnimation());
        }
    }

    public void StopTalking()
    {
        if (isTalking)
        {
            isTalking = false;
            if (talkingCoroutine != null)
            {
                StopCoroutine(talkingCoroutine);
                talkingCoroutine = null;
            }
            // Reset all phoneme blendshapes to 0
            ResetPhonemeBlendshapes();
        }
    }

    private System.Collections.IEnumerator TalkingAnimation()
    {
        float phonemeChangeTimer = 0f;
        float phonemeChangeDuration = 0.1f; // How often to pick new target phonemes
        
        while (isTalking)
        {
            phonemeChangeTimer += Time.deltaTime;
            
            // Pick new target phonemes periodically
            if (phonemeChangeTimer >= phonemeChangeDuration)
            {
                SetNewTargetPhonemes();
                phonemeChangeTimer = 0f;
                phonemeChangeDuration = Random.Range(0.08f, 0.25f); // Vary the timing
            }
            
            // Smoothly interpolate current weights towards target weights
            for (int i = 0; i < PHONEME_COUNT; i++)
            {
                currentPhonemeWeights[i] = Mathf.Lerp(currentPhonemeWeights[i], targetPhonemeWeights[i], Time.deltaTime * 8f);
                headMeshRenderer.SetBlendShapeWeight(PHONEME_START_INDEX + i, currentPhonemeWeights[i]);
            }
            
            yield return null; // Wait for next frame
        }
    }
    
    private void SetNewTargetPhonemes()
    {
        // Reset all targets to 0 first
        for (int i = 0; i < PHONEME_COUNT; i++)
        {
            targetPhonemeWeights[i] = 0f;
        }
        
        // Set 1-3 random phonemes to random weights for more natural combinations
        int phonemesToActivate = Random.Range(1, 4);
        for (int i = 0; i < phonemesToActivate; i++)
        {
            int randomIndex = Random.Range(0, PHONEME_COUNT);
            targetPhonemeWeights[randomIndex] = Random.Range(20f, 100f);
        }
    }

    private void ResetPhonemeBlendshapes()
    {
        for (int i = 0; i < PHONEME_COUNT; i++)
        {
            currentPhonemeWeights[i] = 0f;
            targetPhonemeWeights[i] = 0f;
            headMeshRenderer.SetBlendShapeWeight(PHONEME_START_INDEX + i, 0f);
        }
    }
}
