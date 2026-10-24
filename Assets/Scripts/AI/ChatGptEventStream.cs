using System;
using System.Collections.Generic;
using System.Text;

// Bounded SSE framing shared by chat and the HTTP verifier. Publish only completed output.
public sealed class ChatGptEventStream
{
    readonly StringBuilder pending = new StringBuilder();
    readonly StringBuilder data = new StringBuilder();
    readonly StringBuilder output = new StringBuilder();
    int received;
    public bool Completed { get; private set; }
    public string Text => output.ToString();
    public void Push(string chunk)
    {
        received += Encoding.UTF8.GetByteCount(chunk);
        if(received>256*1024) throw new ArgumentException("ChatGPT event stream was too large.");
        foreach(char ch in chunk) {
            if(ch!='\n') { pending.Append(ch); continue; }
            string line=pending.ToString().TrimEnd('\r'); pending.Clear();
            if(line.Length==0) {
                if(data.Length>0) { Event(data.ToString()); data.Clear(); }
            } else if(line.StartsWith("data:",StringComparison.Ordinal)) {
                if(data.Length>0)data.Append('\n');
                string value=line.Substring(5); if(value.StartsWith(" ",StringComparison.Ordinal))value=value.Substring(1);
                data.Append(value);
            }
        }
    }
    void Event(string json)
    {
        if(json=="[DONE]") return;
        if(Completed) throw new ArgumentException("Unexpected event after response completion.");
        var item=ChatGptJson.Object(json);
        string type=ChatGptJson.Text(item,"type");
        if(type=="response.output_text.delta") {
            if(!(item.TryGetValue("delta",out var delta)&&delta is string text))throw new ArgumentException("Invalid chat stream.");
            output.Append(text); if(output.Length>8192)throw new ArgumentException("ChatGPT output was too large.");
        } else if(type=="response.completed") {
            if(!(item.TryGetValue("response",out var response)&&response is Dictionary<string,object> body) || ChatGptJson.Text(body,"status")!="completed")
                throw new ArgumentException("ChatGPT returned an incomplete response.");
            Completed=true;
        } else if(type=="response.failed"||type=="response.incomplete"||type=="error") throw new ArgumentException("ChatGPT could not complete the response. Retry later.");
    }
}
