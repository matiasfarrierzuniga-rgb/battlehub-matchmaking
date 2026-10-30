// Only insert missing demo rooms; never overwrite a room changed by the service.
const database = db.getSiblingDB(process.env.MONGO_DATABASE);
const now = new Date();
for (const gameType of ["typing", "trivia", "memory"]) {
  const id = `demo-${gameType}`;
  const host = `demo-host-${gameType}`;
  database.matches.updateOne(
    { _id: id },
    { $setOnInsert: {
      Title: `Sala demo ${gameType}`,
      GameType: gameType,
      CreatedBy: host,
      CreatedAt: now,
      LastActivityAt: now,
      MaxPlayers: 4,
      Status: 0,
      Revision: NumberLong("0"),
      Participants: [{ UserId: host, DisplayName: `Host demo ${gameType}`, JoinedAt: now, LastHeartbeatAt: now }]
    } },
    { upsert: true }
  );
}
print(`Datos demo disponibles en ${database.getName()}.matches`);
