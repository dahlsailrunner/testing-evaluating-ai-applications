namespace CarvedRock.Agent;

public record ChatTurn(string Role, string Content);

public record AgentChatRequest(string Message, List<ChatTurn>? History);
