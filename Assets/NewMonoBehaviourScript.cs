using Claudia;
using System;
using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    async void Start()
    {
        var anthropic = new Anthropic()
        {
            ApiKey = "YOUR API KEY"
        };

        var message = await anthropic.Messages.CreateAsync(new()
        {
            Model = Models.Claude3Opus,
            MaxTokens = 1024,
            Messages = new Message[] { new() { Role = "user", Content = "Hello, Claude" } }
        });

        Debug.Log("Assistant: " + message);
    }
}