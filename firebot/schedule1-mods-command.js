// Firebot custom script: adds the "!mods" chat command, which posts links to the Schedule I mods.
// Safe to run again: it replaces the command it created earlier (same id) and nothing else.

const { randomUUID } = require("crypto");

const COMMAND_ID = "5c1e0f10-51a1-4c0f-9a6e-51c4ed0c0a04";

const MESSAGE =
    "The Schedule I mods on stream are free and open source. " +
    "Courier (ship your own stock between properties): https://github.com/purplehaxttv/schedule1-supply-run | " +
    "Bigger ATM, Cuke machine and recycler payouts: https://github.com/purplehaxttv/schedule1-fixed-cash-drops | " +
    "Chat redeems (Firebot): https://github.com/purplehaxttv/schedule1-chat-fx";

module.exports = {
    getScriptManifest: () => ({
        name: "Schedule I mods command",
        description: "Adds the !mods chat command with links to the Schedule I mods.",
        author: "purplehaxttv",
        version: "1.0.0",
        firebotVersion: "5"
    }),

    getDefaultParameters: () => ({}),

    run: async (runRequest) => {
        const { modules } = runRequest;
        modules.commandManager.saveImportedCustomCommand({
            id: COMMAND_ID,
            type: "custom",
            trigger: "!mods",
            description: "Links to the Schedule I mods used on stream.",
            active: true,
            simple: false,
            aliases: ["!s1mods", "!schedule1mods"],
            sortTags: [],
            cooldown: { user: 0, global: 30 },
            sendCooldownMessage: false,
            cooldownMessage: "This command is still on cooldown for: {timeLeft}",
            sendCooldownMessageAsReply: true,
            treatQuotedTextAsSingleArg: false,
            allowTriggerBySharedChat: "inherit",
            restrictionData: { restrictions: [], mode: "all", sendFailMessage: true },
            effects: {
                id: randomUUID(),
                list: [{ id: randomUUID(), type: "firebot:chat", active: true, chatter: "Bot", message: MESSAGE, percentWeight: null }]
            }
        });
        modules.commandManager.triggerUiRefresh();
        modules.logger.info("[S1 mods command] saved !mods");
        return { success: true, effects: [] };
    }
};
