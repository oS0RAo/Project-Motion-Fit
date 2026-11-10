using UnityEngine;
using UnityEngine.InputSystem;

/// Attach to the Player. Press E near an NPC to talk.
public class PlayerInteractor : MonoBehaviour
{
  [SerializeField] DialogueUI ui;
  [SerializeField] LayerMask npcLayer = ~0;
  [SerializeField] GameObject promptHint;   // optional "Press E" UI

  NpcDialogue nearest;
  readonly Collider[] hits = new Collider[8];

  void Update()
  {
    if (ui == null) return;

    if (ui.IsOpen) { if (promptHint) promptHint.SetActive(false); return; }

    nearest = FindNearest();
    if (promptHint) promptHint.SetActive(nearest != null);

    var keyboard = Keyboard.current;

    if (nearest != null &&
        keyboard != null &&
        keyboard.eKey.wasPressedThisFrame)
    {
      ui.Open(nearest);
    }

  }

  NpcDialogue FindNearest()
  {
    int n = Physics.OverlapSphereNonAlloc(
    transform.position,
    3f,
    hits,
    npcLayer,
    QueryTriggerInteraction.Collide);
    ;
    NpcDialogue best = null; float bestD = float.MaxValue;
    for (int i = 0; i < n; i++)
    {
      var npc = hits[i].GetComponentInParent<NpcDialogue>();
      if (!npc) continue;
      float d = (npc.transform.position - transform.position).sqrMagnitude;
      if (d < bestD && d <= npc.interactRange * npc.interactRange) { best = npc; bestD = d; }
    }
    return best;
  }
}