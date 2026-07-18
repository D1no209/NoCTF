# NoCTF repository instructions

## FastEndpoints

- Every HTTP endpoint must derive from a strongly typed FastEndpoints base such as `Endpoint<TRequest, TResponse>` or `EndpointWithoutRequest<TResponse>`.
- Implement endpoint behavior with `ExecuteAsync`; do not implement business endpoints with `HandleAsync`.
- Model alternative HTTP outcomes with `Microsoft.AspNetCore.Http.HttpResults.Results<T1, ..., Tn>`.
- Construct outcomes with `TypedResults` (`TypedResults.Ok`, `TypedResults.Accepted`, `TypedResults.NotFound`, `TypedResults.Problem`, and similar methods).
- Use concrete typed result types such as `Ok<T>`, `Accepted<T>`, `Created<T>`, `NotFound`, `NoContent`, `UnauthorizedHttpResult`, and `ProblemHttpResult` in the endpoint response declaration.
- Do not manually serialize endpoint responses, set status codes, or call `HttpResponse.Send*Async`/`WriteAsJsonAsync` from endpoint execution code.
- File-upload endpoints must bind files through a request DTO (`IFormFile`/`IReadOnlyList<IFormFile>`) and opt in with `AllowFileUploads()`. Do not read `HttpContext.Request.Body` from an endpoint. Use `FormFileSectionsAsync()` only when streaming is explicitly required for a large-file contract.
- Use domain/application enums for bounded concepts (for example submission kind and runtime provider). Convert to protocol text only at an explicit external boundary; do not pass provider/kind strings through ports or adapters.
- Never hand-edit EF Core migrations or the model snapshot. Create, remove, and update migrations only with `dotnet ef migrations ...`; generated migration files are tooling-owned.
- In AWDP, a Break achievement is produced by a correctly evaluated Flag submission; Fix is a separate archive submission and must not be modeled as another Flag action.
- Keep route binding, authentication/authorization metadata, rate limiting, and transport mapping in the endpoint. Put business rules in Application use cases.
- Each endpoint lives in its own file. Endpoint-specific request/response models may share that file; models reused by multiple endpoints belong in a sibling `*Models.cs` file.
