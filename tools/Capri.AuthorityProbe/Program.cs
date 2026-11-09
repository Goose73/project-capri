using System.Text.Json;
using Capri.Core.Autonomy.Authority;
using Capri.Security.Autonomy.Authority;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: probe <pipeName> <command> [args...]");
    return 2;
}

var pipeName = args[0];
var command = args[1];
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
try
{
    await using var client = new NamedPipeTaskAuthorityClient(pipeName, "authority-probe-" + Environment.ProcessId, canApprove: true, connectTimeout: TimeSpan.FromSeconds(5));
    await client.EnsureConnectedAsync();
    switch (command)
    {
        case "get-state":
        {
            var resp = await client.SendAsync(new AuthorityRequest(client.NextId(), AuthorityRequestKind.GetState));
            Console.WriteLine(JsonSerializer.Serialize(resp, json));
            return resp.Ok ? 0 : 1;
        }
        case "list-tasks":
        {
            var resp = await client.SendAsync(new AuthorityRequest(client.NextId(), AuthorityRequestKind.ListTasks, MaxCount: 20));
            Console.WriteLine(JsonSerializer.Serialize(resp, json));
            return resp.Ok ? 0 : 1;
        }
        case "stop":
        {
            var resp = await client.SendAsync(new AuthorityRequest(client.NextId(), AuthorityRequestKind.Stop));
            Console.WriteLine(JsonSerializer.Serialize(resp, json));
            return resp.Ok ? 0 : 1;
        }
        case "reset-stop":
        {
            var resp = await client.SendAsync(new AuthorityRequest(client.NextId(), AuthorityRequestKind.ResetStop));
            Console.WriteLine(JsonSerializer.Serialize(resp, json));
            return resp.Ok ? 0 : 1;
        }
        case "submit-approve-wait":
        {
            // args[2]=goal, args[3]=workspace(optional)
            var goal = args.Length > 2 ? args[2] : "probe task";
            var workspace = args.Length > 3 ? args[3] : null;
            var submit = await client.SendAsync(new AuthorityRequest(client.NextId(), AuthorityRequestKind.SubmitTask, Goal: goal));
            if (!submit.Ok) { Console.WriteLine(JsonSerializer.Serialize(submit, json)); return 1; }
            var taskId = submit.Task?.TaskId;
            var phases = new List<string>();
            string? result = "timeout";
            Capri.Core.Autonomy.TaskProgress? lastProgress = null;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await foreach (var evt in client.Events(cts.Token))
            {
                if (evt.Approval is { } approval && approval.TaskId == taskId)
                {
                    var resp = await client.SendAsync(new AuthorityRequest(client.NextId(), AuthorityRequestKind.Approve,
                        TaskId: approval.TaskId, ApprovalId: approval.ApprovalId, PlanHash: approval.PlanHash, Workspace: workspace), cts.Token);
                    phases.Add("approve:" + resp.Ok);
                }
                if (evt.Progress is { } p && (p.TaskId == taskId || taskId is null)) { phases.Add(p.Phase.ToString()); lastProgress = p; }
                if (evt.Kind == AuthorityEventKind.TaskCompleted && (evt.Task?.TaskId == taskId || taskId is not null)) { result = evt.Task?.Phase ?? "completed"; break; }
                if (lastProgress is { Phase: Capri.Core.Autonomy.TaskPhase.Completed or Capri.Core.Autonomy.TaskPhase.Failed or Capri.Core.Autonomy.TaskPhase.Cancelled }) { result = lastProgress.Phase.ToString(); break; }
            }
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, taskId, phases, result }, json));
            return result is "Completed" ? 0 : 1;
        }
        case "submit-bad-hash":
        {
            var goal = args.Length > 2 ? args[2] : "probe task";
            var submit = await client.SendAsync(new AuthorityRequest(client.NextId(), AuthorityRequestKind.SubmitTask, Goal: goal));
            if (!submit.Ok) { Console.WriteLine(JsonSerializer.Serialize(submit, json)); return 1; }
            var taskId = submit.Task?.TaskId;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await foreach (var evt in client.Events(cts.Token))
            {
                if (evt.Approval is { } approval && approval.TaskId == taskId)
                {
                    var resp = await client.SendAsync(new AuthorityRequest(client.NextId(), AuthorityRequestKind.Approve,
                        TaskId: approval.TaskId, ApprovalId: approval.ApprovalId, PlanHash: "deadbeef", Workspace: null), cts.Token);
                    // deny properly so server can finish
                    var deny = await client.SendAsync(new AuthorityRequest(client.NextId(), AuthorityRequestKind.Deny,
                        TaskId: approval.TaskId, ApprovalId: approval.ApprovalId, PlanHash: approval.PlanHash), cts.Token);
                    Console.WriteLine(JsonSerializer.Serialize(new { badHashOk = resp.Ok, badHashError = resp.Error, denyOk = deny.Ok }, json));
                    return resp.Ok ? 1 : 0;
                }
            }
            return 1;
        }
        case "hold":
        {
            // stay connected until stdin closes
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, pid = Environment.ProcessId }, json));
            Console.Out.Flush();
            Console.ReadLine();
            return 0;
        }
        default:
            Console.Error.WriteLine("unknown command");
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
    return 1;
}
