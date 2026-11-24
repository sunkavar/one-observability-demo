#nullable enable
using System.Diagnostics;

namespace PetSite.Helpers
{
    /// <summary>Copies user.id from the nearest parent span onto every span the app starts.</summary>
    public static class UserIdPropagation
    {
        private const string UserIdTag = "user.id";
        private static ActivityListener? _listener;

        public static void Start()
        {
            // No Sample callback, so this listener never changes which spans are recorded.
            _listener = new ActivityListener
            {
                ShouldListenTo = _ => true,
                ActivityStarted = CopyFromParent,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        private static void CopyFromParent(Activity activity)
        {
            if (activity.GetTagItem(UserIdTag) != null)
            {
                return;
            }
            for (var parent = activity.Parent; parent != null; parent = parent.Parent)
            {
                if (parent.GetTagItem(UserIdTag) is string userId && userId.Length > 0)
                {
                    activity.SetTag(UserIdTag, userId);
                    return;
                }
            }
        }
    }
}
