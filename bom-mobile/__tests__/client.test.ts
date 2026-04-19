import MockAdapter from "axios-mock-adapter";
import { api, __resetRefreshState } from "@/api/client";
import { saveTokens, getAccess } from "@/auth/secureStore";

let mock: MockAdapter;

beforeEach(async () => {
  mock = new MockAdapter(api);
  __resetRefreshState();
  await saveTokens("old-access", "refresh-1");
});

afterEach(() => {
  mock.restore();
});

test("401 triggers refresh and retries original request", async () => {
  mock.onGet("/api/ping")
    .replyOnce(401, { code: "token_expired" })
    .onGet("/api/ping")
    .replyOnce(200, { ok: true });

  mock.onPost("/api/auth/refresh").reply(200, {
    accessToken: "new-access",
    refreshToken: "refresh-2",
  });

  const res = await api.get("/api/ping");
  expect(res.data).toEqual({ ok: true });
  expect(await getAccess()).toBe("new-access");
});

test("concurrent 401s share one refresh call", async () => {
  let refreshCount = 0;
  mock.onPost("/api/auth/refresh").reply(() => {
    refreshCount++;
    return [200, { accessToken: "new-access", refreshToken: "refresh-2" }];
  });
  mock.onGet("/api/a").replyOnce(401, { code: "token_expired" }).onGet("/api/a").reply(200, { a: 1 });
  mock.onGet("/api/b").replyOnce(401, { code: "token_expired" }).onGet("/api/b").reply(200, { b: 2 });

  const [a, b] = await Promise.all([api.get("/api/a"), api.get("/api/b")]);
  expect(a.data).toEqual({ a: 1 });
  expect(b.data).toEqual({ b: 2 });
  expect(refreshCount).toBe(1);
});

test("refresh failure clears tokens and rejects", async () => {
  mock.onGet("/api/ping").reply(401, { code: "token_expired" });
  mock.onPost("/api/auth/refresh").reply(401);

  await expect(api.get("/api/ping")).rejects.toBeDefined();
  expect(await getAccess()).toBeNull();
});
