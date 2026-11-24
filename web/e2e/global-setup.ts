import net from "node:net";

/**
 * Optional TCP forwards, for running inside a container on the compose network. The app sets Secure cookies and
 * links to the payment sandbox at its public URL, so both must be reachable as `localhost` from the browser.
 * E2E_FORWARD="3000=web:3000,18090=payments:8080" listens on localhost:3000 and forwards to web:3000, and so on.
 */
export default async function globalSetup() {
  const spec = process.env.E2E_FORWARD?.trim();
  if (!spec) return;

  const servers = await Promise.all(
    spec.split(",").map((entry) => {
      const [local, target] = entry.split("=");
      const [host, port] = target.split(":");
      const server = net.createServer((socket) => {
        const upstream = net.connect(Number(port), host);
        socket.pipe(upstream).pipe(socket);
        socket.on("error", () => upstream.destroy());
        upstream.on("error", () => socket.destroy());
      });
      return new Promise<net.Server>((resolve, reject) => {
        server.once("error", reject);
        server.listen(Number(local), "127.0.0.1", () => resolve(server));
      });
    }),
  );

  return async () => {
    await Promise.all(servers.map((server) => new Promise((resolve) => server.close(resolve))));
  };
}
