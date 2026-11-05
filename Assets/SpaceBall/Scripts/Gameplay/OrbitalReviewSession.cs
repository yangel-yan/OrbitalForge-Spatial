using UnityEngine;

namespace SpaceBall
{
    public sealed partial class OrbitalPrototype
    {
        // Explicit development-build intents make native rendering and real
        // physics reproducible without unlocking or completing the player's save.
        // This path is absent from non-development Android builds.
        public bool IsReviewSession { get; private set; }
        int reviewLevel = -1;
        bool reviewArrange, reviewLaunch;
        bool reviewBypass;
        float reviewDelay, reviewDeadline = -1f;

        void InitializeReviewSession()
        {
#if DEVELOPMENT_BUILD && UNITY_ANDROID
            if (!Debug.isDebugBuild) return;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                int level = intent.Call<int>("getIntExtra", "spaceball.review.level", -1);
                if (!OrbitalCampaign.IsValid((OrbitalLevel)level)) return;
                reviewLevel = level;
                reviewArrange = intent.Call<bool>("getBooleanExtra", "spaceball.review.arrange", false);
                reviewLaunch = intent.Call<bool>("getBooleanExtra", "spaceball.review.launch", false);
                reviewBypass = intent.Call<bool>("getBooleanExtra", "spaceball.review.bypass", false);
                reviewDelay = Mathf.Clamp(intent.Call<int>("getIntExtra", "spaceball.review.delay", 0), 0, 180);
                IsReviewSession = true;
                progress = new OrbitalProgress { sound = Profile.sound, reducedMotion = Profile.reducedMotion, trace = Profile.trace };
                for (int i = 0; i < level; i++) progress.For((OrbitalLevel)i).Complete(1, true);
                Debug.Log("[OrbitalReview] isolated native review, level=" + level);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[OrbitalReview] Review intent unavailable: " + exception.Message);
            }
#endif
        }

        void Start()
        {
#if DEVELOPMENT_BUILD && UNITY_ANDROID
            if (!IsReviewSession) return;
            if (reviewArrange)
            {
                if (Construction != null)
                {
                    ApplyConstructionReferenceSolution(State.Level);
                    if (reviewBypass && State.Level == OrbitalLevel.Finale)
                    {
                        int decision = OrbitalConstructionLayout.For(State.Level).DecisionSocket;
                        foreach (var tile in Construction.tiles)
                            if (tile.SocketIndex == decision) { Construction.Select(tile.index); Construction.ReturnSelectedToTray(); }
                        foreach (var tile in Construction.tiles)
                            if (tile.shape == OrbitalTileShape.CornerLeft) { Construction.Place(tile.index, decision, 3); break; }
                    }
                }
                else if (Assembly != null)
                {
                    Assembly.Place(0); Assembly.Place(1);
                    ConfigureRoute(OrbitalCampaign.CorrectRoute(State.Level));
                    ConfigureReflector(OrbitalCampaign.CorrectReflector(State.Level));
                }
            }
            if (reviewLaunch)
            {
                reviewDeadline = Time.unscaledTime + reviewDelay;
                Debug.Log("[OrbitalReview] physical flight scheduled, delay=" + reviewDelay);
            }
#endif
        }

        void TickReviewLaunch()
        {
#if DEVELOPMENT_BUILD && UNITY_ANDROID
            if (IsReviewSession && reviewDeadline >= 0 && Time.unscaledTime >= reviewDeadline && State.Phase == OrbitalPhase.Configuring)
            {
                reviewDeadline = -1f;
                bool launched = Launch();
                Debug.Log("[OrbitalReview] physical flight " + (launched ? "launched" : "rejected") + ", level=" + State.Level);
            }
#endif
        }

        void LogReviewResult()
        {
#if DEVELOPMENT_BUILD && UNITY_ANDROID
            if (IsReviewSession)
                Debug.Log("[OrbitalReview] result=" + State.ResultCode + " level=" + State.Level +
                    " elapsed=" + shotElapsed.ToString("F2") + " A=" + State.HasA + " B=" + State.HasB +
                    " G=" + State.GateCrossed + " C=" + State.HasC);
#endif
        }
    }
}
