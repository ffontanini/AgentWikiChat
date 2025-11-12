namespace AgentWikiChat.Models;

public class Message
{
    public string Role { get; set; }
    public string Content { get; set; }
    public DateTime Timestamp { get; set; }
    public string? ToolCallId { get; set; }
    public List<ToolCall>? ToolCalls { get; set; }

    public Message(string role, string content)
    {
        Role = role;
        Content = content;
        Timestamp = DateTime.Now;
    }

    public Message(string role, string content, string? toolCallId)
    {
        Role = role;
        Content = content;
        ToolCallId = toolCallId;
        Timestamp = DateTime.Now;
    }

    public Message(string role, string content, List<ToolCall>? toolCalls)
    {
        Role = role;
        Content = content;
        ToolCalls = toolCalls;
        Timestamp = DateTime.Now;
    }
}
