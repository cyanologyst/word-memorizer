using System.Security;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using WordReviewReminder.Core;

namespace WordReviewReminder.Services;

public static class ToastService
{
    public static void Show(WordEntry word)
    {
        try
        {
            var meaning = string.IsNullOrWhiteSpace(word.ShortMeaning) ? "Review this word now." : word.ShortMeaning;
            var xml = $"""
                      <toast launch="review">
                        <visual>
                          <binding template="ToastGeneric">
                            <text>{Escape(word.Term)}</text>
                            <text>{Escape($"{word.PartOfSpeech} {word.Pronunciation}".Trim())}</text>
                            <text>{Escape(meaning)}</text>
                          </binding>
                        </visual>
                      </toast>
                      """;

            var document = new XmlDocument();
            document.LoadXml(xml);
            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(document));
        }
        catch
        {
            // Native toasts can fail if the app is launched without package identity.
        }
    }

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }
}
