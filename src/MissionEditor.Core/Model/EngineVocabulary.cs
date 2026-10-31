namespace MissionEditor.Core.Model;

/// <summary>
/// The condition and action keys content.dll understands in a [Trigger] section (factory tables
/// of FUN_06ebb850, see fl_decomp MissionScript.cpp). Keys are compared case-insensitively; an
/// unknown Cnd_/Act_ key is silently ignored by the engine.
/// </summary>
public static class EngineVocabulary
{
  /// <summary>All known condition keys.</summary>
  public static readonly IReadOnlySet<string> Conditions = new HashSet<string> (StringComparer.OrdinalIgnoreCase)
    {
        "Cnd_BaseEnter", "Cnd_BaseExit", "Cnd_CargoScanned", "Cnd_CharSelect", "Cnd_CommComplete",
        "Cnd_CmpToPlane", "Cnd_Destroyed", "Cnd_DistCircle", "Cnd_DistShip", "Cnd_DistVec",
        "Cnd_DistVecLbl", "Cnd_EncLaunched", "Cnd_HasMsn", "Cnd_HealthDec", "Cnd_InSpace",
        "Cnd_InTradelane", "Cnd_InZone", "Cnd_JumpgateAct", "Cnd_JumpInComplete", "Cnd_LaunchComplete",
        "Cnd_LocEnter", "Cnd_LocExit", "Cnd_LootAcquired", "Cnd_MsnResponse", "Cnd_NPCSystemEnter",
        "Cnd_NPCSystemExit", "Cnd_PlayerLaunch", "Cnd_PlayerManeuver", "Cnd_PopUpDialog", "Cnd_ProjHit",
        "Cnd_ProjHitShipToLbl", "Cnd_RTCDone", "Cnd_RumorHeard", "Cnd_SpaceEnter", "Cnd_SpaceExit",
        "Cnd_SystemEnter", "Cnd_SystemExit", "Cnd_TetherBroke", "Cnd_Timer", "Cnd_TLEntered",
        "Cnd_TLExited", "Cnd_True", "Cnd_WatchTrigger", "Cnd_WatchVibe",
    };

  /// <summary>All known action keys.</summary>
  public static readonly IReadOnlySet<string> Actions = new HashSet<string> (StringComparer.OrdinalIgnoreCase)
    {
        "Act_ActTrig", "Act_AddAmbient", "Act_AddRTC", "Act_AdjAcct", "Act_AdjHealth", "Act_CallThorn",
        "Act_ChangeState", "Act_Cloak", "Act_DeactTrig", "Act_DebugMsg", "Act_Destroy", "Act_DisableEnc",
        "Act_DisableFriendlyFire", "Act_DisableTradelane", "Act_DockRequest", "Act_EnableEnc",
        "Act_EnableManeuver", "Act_EtherComm", "Act_ForceLand", "Act_GCSClamp", "Act_GiveMB",
        "Act_GiveNNObjs", "Act_GiveObjList", "Act_HostileClamp", "Act_Invulnerable", "Act_Jumper",
        "Act_LightFuse", "Act_LockDock", "Act_LockManeuvers", "Act_MarkObj", "Act_MovePlayer",
        "Act_NagClamp", "Act_NagDistLeaving", "Act_NagDistTowards", "Act_NagGreet", "Act_NagOff",
        "Act_NNPath", "Act_NNIds", "Act_PlayerCanDock", "Act_PlayerCanTradelane", "Act_PlayerEnemyClamp",
        "Act_PlayerForm", "Act_PlayMusic", "Act_PlayNN", "Act_PlaySoundEffect", "Act_PilotParams",
        "Act_PobjIdle", "Act_PopUpDialog", "Act_SetPriority", "Act_RandomPop", "Act_RandomPopSphere",
        "Act_RelocateForm", "Act_RelocateShip", "Act_RemoveAmbient", "Act_RemoveCargo", "Act_RemoveRTC",
        "Act_RepChangeRequest", "Act_RevertCam", "Act_RpopAttClamp", "Act_RpopTLAttacksEnabled", "Act_Save",
        "Act_SendComm", "Act_SetFlee", "Act_SetInitialPlayerPos", "Act_SetLifeTime", "Act_SetNNHidden",
        "Act_SetNNObj", "Act_SetNNState", "Act_SetOffer", "Act_SetOrient", "Act_SetRep",
        "Act_SetShipAndLoadout", "Act_SetTitle", "Act_SetVibe", "Act_SetVibeLbl", "Act_SetVibeLblToShip",
        "Act_SetVibeShipToLbl", "Act_SetVibeOfferBaseHack", "Act_SpawnFormation", "Act_SpawnLoot",
        "Act_SpawnShip", "Act_SpawnShipRel", "Act_SpawnSolar", "Act_StartDialog", "Act_StaticCam",
    };

  /// <summary>True for a key that starts with "Cnd_" (case-insensitive).</summary>
  public static bool LooksLikeCondition (string key) => key.StartsWith ("Cnd_", StringComparison.OrdinalIgnoreCase);

  /// <summary>True for a key that starts with "Act_" (case-insensitive).</summary>
  public static bool LooksLikeAction (string key) => key.StartsWith ("Act_", StringComparison.OrdinalIgnoreCase);
}
