using System;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class DialogueUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] GameObject panel;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text bodyText;
    [SerializeField] TMP_InputField input;
    [SerializeField] Button sendButton;

    [Header("Streaming")]
    [SerializeField, Min(0.02f)]
    float refreshInterval = 0.05f;

    [SerializeField, Min(1)]
    int maxInputCharacters = 300;

    public static event Action<bool> OnDialogueToggled;
    public bool IsOpen { get; private set; }

    readonly StringBuilder replyBuffer = new();

    NpcDialogue current;
    CancellationTokenSource requestCancellation;

    bool busy;
    bool dirty;
    int version;
    float nextRefresh;

    CursorLockMode previousLockMode;
    bool previousCursorVisible;

    void Awake()
    {
        // Put this component on an always-active object,
        // NOT on the panel that gets hidden.
        panel.SetActive(false);

        input.characterLimit = maxInputCharacters;
        input.lineType = TMP_InputField.LineType.SingleLine;

        // Treat AI output as plain text, not TMP markup.
        bodyText.richText = false;
        bodyText.maxVisibleCharacters = int.MaxValue;

        sendButton.onClick.AddListener(Submit);
        input.onSubmit.AddListener(OnInputSubmit);
    }

    void Update()
    {
        if (!IsOpen)
            return;

        var keyboard = Keyboard.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        if (dirty && Time.unscaledTime >= nextRefresh)
        {
            bodyText.text = replyBuffer.ToString();
            dirty = false;
            nextRefresh = Time.unscaledTime + refreshInterval;
        }
    }

    void OnInputSubmit(string _)
    {
        Submit();
    }

    public void Open(NpcDialogue npc)
    {
        if (npc == null || npc.persona == null)
            return;

        if (IsOpen)
            Close();

        previousLockMode = Cursor.lockState;
        previousCursorVisible = Cursor.visible;

        version++;
        current = npc;
        IsOpen = true;
        busy = false;
        dirty = false;

        replyBuffer.Clear();
        panel.SetActive(true);

        nameText.text = npc.persona.npcName;
        bodyText.text = npc.persona.greeting;
        bodyText.maxVisibleCharacters = int.MaxValue;

        input.text = "";
        SetInteractable(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        OnDialogueToggled?.Invoke(true);
        input.ActivateInputField();
    }

    public void Close()
    {
        if (!IsOpen)
            return;

        // Invalidate callbacks from an older conversation.
        version++;
        IsOpen = false;

        requestCancellation?.Cancel();
        requestCancellation = null;

        busy = false;
        dirty = false;
        current = null;

        panel.SetActive(false);

        Cursor.lockState = previousLockMode;
        Cursor.visible = previousCursorVisible;

        OnDialogueToggled?.Invoke(false);
    }

    async void Submit()
    {
        if (!IsOpen || busy || current == null)
            return;

        string text = input.text.Trim();

        if (string.IsNullOrEmpty(text))
            return;

        var npc = current;
        int requestVersion = ++version;

        var cancellation = new CancellationTokenSource();
        requestCancellation = cancellation;

        busy = true;
        dirty = false;
        nextRefresh = 0f;
        replyBuffer.Clear();

        input.text = "";
        SetInteractable(false);
        bodyText.text = "กำลังคิด…";

        bool IsCurrentRequest()
        {
            return this != null &&
                   IsOpen &&
                   version == requestVersion &&
                   current == npc;
        }

        try
        {
            string reply = await npc.AskAsync(
                text,
                chunk =>
                {
                    if (!IsCurrentRequest())
                        return;

                    replyBuffer.Append(chunk);
                    dirty = true;
                },
                cancellation.Token);

            if (IsCurrentRequest())
            {
                dirty = false;
                bodyText.text = reply;
            }
        }
        catch (OperationCanceledException)
        {
            // Closing the dialogue is a normal cancellation.
        }
        catch (Exception exception)
        {
            if (this != null)
                Debug.LogException(exception);

            if (IsCurrentRequest())
            {
                dirty = false;

                string fallback = npc != null && npc.persona != null
                    ? npc.persona.fallbackLine
                    : "ขอเวลาสักครู่ แล้วลองคุยใหม่อีกครั้งนะ";

                // Do not present an interrupted answer as complete.
                bodyText.text = replyBuffer.Length > 0
                    ? replyBuffer + "\n\n[การตอบกลับขาดช่วง กรุณาลองใหม่]"
                    : fallback;
            }
        }
        finally
        {
            if (this != null &&
                ReferenceEquals(requestCancellation, cancellation))
            {
                requestCancellation = null;
            }

            cancellation.Dispose();

            if (IsCurrentRequest())
            {
                busy = false;
                SetInteractable(true);
                input.ActivateInputField();
            }
        }
    }

    void SetInteractable(bool value)
    {
        input.interactable = value;
        sendButton.interactable = value;
    }

    void OnDisable()
    {
        Close();
    }

    void OnDestroy()
    {
        if (sendButton != null)
            sendButton.onClick.RemoveListener(Submit);

        if (input != null)
            input.onSubmit.RemoveListener(OnInputSubmit);
    }
}