import { describe, it, expect } from "vitest";
import { urlBase64ToUint8Array } from "./vapid";

describe("urlBase64ToUint8Array", () => {
  it("decodes a standard base64url string", () => {
    const out = urlBase64ToUint8Array("SGVsbG8");
    expect(Array.from(out)).toEqual([72, 101, 108, 108, 111]);
  });

  it("handles URL-safe characters", () => {
    const out = urlBase64ToUint8Array("--__");
    expect(Array.from(out)).toEqual([251, 239, 255]);
  });

  it("decodes a typical VAPID public key (87 chars, no padding)", () => {
    const vapid = "BNxPP9PhIxBjaHv4WdpFrApT7ot3YTeNW0z_uG44VZh3MqcJVDmZ-2I2qRtm6gwKfL0wvtmgrrHpLgSsOQE0aHs";
    const out = urlBase64ToUint8Array(vapid);
    expect(out.length).toBe(65);
    expect(out[0]).toBe(0x04);
  });
});
