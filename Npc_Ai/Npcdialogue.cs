using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using static LlmClient;

public class NpcDialogue : MonoBehaviour
{
  public NpcPersona persona;
  public LlmClient client;

  [Min(0.1f)]
  public float interactRange = 2.5f;

  readonly List<ChatMessage> history = new();
  bool busy;

  public async Awaitable<string> AskAsync(
      string playerText,
      Action<string> onChunk = null,
      CancellationToken cancellationToken = default)
  {
    if (busy)
      throw new InvalidOperationException("NPC is already responding.");

    if (persona == null || client == null)
      throw new InvalidOperationException("Assign Persona and Client.");

    if (string.IsNullOrWhiteSpace(playerText))
      throw new ArgumentException("Player message is empty.");

    busy = true;

    try
    {
      using var linked = CancellationTokenSource.CreateLinkedTokenSource(
          cancellationToken,
          destroyCancellationToken,
          client.destroyCancellationToken);

      int rememberedTurns = Mathf.Clamp(
          persona.maxTurnsRemembered, 2, 20);

      // Each completed turn contains user + assistant.
      int keepMessages = rememberedTurns * 2;
      int start = Mathf.Max(0, history.Count - keepMessages);

      var requestHistory = new List<ChatMessage>();

      for (int i = start; i < history.Count; i++)
        requestHistory.Add(history[i]);

      string message = playerText.Trim();

      requestHistory.Add(new ChatMessage("user", message));

      string reply = await client.SendStreamAsync(
          persona.BuildSystemPrompt(),
          requestHistory,
          onChunk,
          linked.Token);

      linked.Token.ThrowIfCancellationRequested();

      // Commit only after a successful response.
      history.Add(new ChatMessage("user", message));
      history.Add(new ChatMessage("assistant", reply));

      while (history.Count > keepMessages)
        history.RemoveRange(0, 2);

      return reply;
    }
    finally
    {
      busy = false;
    }
  }

  public void ResetMemory()
  {
    if (!busy)
      history.Clear();
  }
}