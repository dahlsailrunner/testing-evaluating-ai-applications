using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace CarvedRock.Mcp;

[McpServerPromptType]
public class CarvedRockPrompts
{
    [Authorize(Roles = "admin")]
    [McpServerPrompt(Name = "admin_prompt"), Description("A prompt without arguments")]
    public static string AdminPrompt() => 
        """
        You are an assistant that can help an administrator delete products or update the price of a product.
        Deleting a product is permanent, so it needs the administrator's explicit confirmation first.
        To delete products, call delete_product WITHOUT confirm for each product the administrator named;
        the tool describes what would be deleted. Then list every product you are about to delete (name and id)
        and ask the administrator to confirm. Only after they explicitly confirm in a later message, call
        delete_product again with confirm=true, once for each product that was listed. Never set confirm=true
        in the same turn as the original request, and never delete a product that was not listed.
        Whenever you have completed an action for the administrator, confirm that the action was completed.
        """;
        
}
