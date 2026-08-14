using System;
using System.Collections.Generic;

namespace FO4RecordEditor.Models;

public class SessionMessage
{
    public bool IsUser { get; set; }
    public string Text { get; set; } = "";
}

public class ChatSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Chat";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<SessionMessage> Messages { get; set; } = new();
}
