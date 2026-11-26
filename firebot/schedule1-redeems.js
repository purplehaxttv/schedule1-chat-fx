// Firebot custom script: sets up the Schedule I "Chat FX" channel point redeems.
//
// Run it once with the "Run Custom Script" effect. It creates, through Firebot's own reward
// manager (so the rewards are Firebot-owned and fully editable in Firebot afterwards):
//   - a "Schedule I" sort tag for channel rewards
//   - ten channel rewards, created DISABLED, each calling the Chat FX mod over HTTP
//   - two events (Category Changed, Stream Started) that unpause the tagged rewards while the
//     stream category is Schedule I and pause them otherwise
//   - a mod-only "!s1sync" command that applies the same check on demand
//
// It is safe to run again: anything that already exists (matched by name or fixed id) is left
// alone. With "Dry Run" ticked it changes nothing and only writes a report.
//
// It reaches three Firebot managers that are not handed to scripts (channel rewards, sort tags,
// events) by loading them from Firebot's own files. Written against Firebot 5.67.

const path = require("path");
const fs = require("fs");
const { randomUUID } = require("crypto");

const TAG_NAME = "Schedule I";
const TAG_CONTEXT = "channel rewards";
const EVENT_CATEGORY_ID = "5c1e0f10-51a1-4c0f-9a6e-51c4ed0c0a01";
const EVENT_ONLINE_ID = "5c1e0f10-51a1-4c0f-9a6e-51c4ed0c0a02";
const COMMAND_ID = "5c1e0f10-51a1-4c0f-9a6e-51c4ed0c0a03";

function internal(relativePath) {
    return require(path.join(process.resourcesPath, "app.asar", "build", "backend", relativePath));
}

function effectList(list) {
    return { id: randomUUID(), list };
}

function httpEffect(url) {
    return { id: randomUUID(), type: "firebot:http-request", active: true, headers: [], options: {}, method: "GET", url, percentWeight: null };
}

function chatEffect(message) {
    return { id: randomUUID(), type: "firebot:chat", active: true, chatter: "Bot", message, percentWeight: null };
}

function pauseEffect(tagId, paused) {
    return {
        id: randomUUID(),
        type: "firebot:update-channel-reward",
        active: true,
        rewardSelectMode: "sortTag",
        useTag: true,
        sortTagId: tagId,
        rewardSettings: {
            name: { update: false, newValue: "" },
            description: { update: false, newValue: "" },
            cost: { update: false, newValue: 1 },
            enabled: { update: false, newValue: true },
            paused: { update: true, newValue: paused }
        },
        percentWeight: null
    };
}

// "If the stream category is <category>, unpause the tagged rewards, otherwise pause them."
function categoryCheck(tagId, category) {
    return {
        id: randomUUID(),
        type: "firebot:conditional-effects",
        active: true,
        ifs: [{
            conditionData: {
                mode: "exclusive",
                conditions: [{
                    type: "firebot:custom",
                    comparisonType: "is",
                    value: null,
                    leftSideValue: "$lowercase[$category]",
                    rightSideValue: category.toLowerCase()
                }]
            },
            effectData: { id: randomUUID(), list: [pauseEffect(tagId, false)], runMode: "all" }
        }],
        otherwiseEffectData: { id: randomUUID(), list: [pauseEffect(tagId, true)], runMode: "all" },
        percentWeight: null
    };
}

function rewardDefinitions(base, property) {
    const fx = query => `${base}/fx?${query}`;
    const prop = encodeURIComponent(property);
    return [
        // helps
        { title: "S1: Bank Deposit", prompt: "Wire $100 into the crew's bank account.", cost: 500, cooldown: 60, color: "#2E8B57",
            url: fx("type=help&amount=100"), chat: "$user wired $100 to the bank." },
        { title: "S1: Tip", prompt: "Slip the streamer $100 in cash.", cost: 500, cooldown: 60, color: "#2E8B57",
            url: fx("type=tip&amount=100"), chat: "$user slipped the streamer $100 cash." },
        { title: "S1: Pardon", prompt: "Call off the cops. Clears the wanted level.", cost: 1000, cooldown: 120, color: "#2E8B57",
            url: fx("type=pardon"), chat: "$user paid off the cops. Wanted level cleared." },
        { title: "S1: Gift", prompt: "Hand the streamer 10 more of a product they are carrying.", cost: 1500, cooldown: 120, color: "#2E8B57",
            url: fx("type=gift&amount=10"), chat: "$user handed over 10 product." },
        { title: "S1: Supply Drop", prompt: `Drop 20 product at the ${property}.`, cost: 2000, cooldown: 180, color: "#2E8B57",
            url: fx(`type=restock&property=${prop}&amount=20`), chat: `$user sent a supply drop to the ${property}.` },
        // hurts
        { title: "S1: Call the Cops", prompt: "Tip off the police. The streamer gets a wanted level.", cost: 2500, cooldown: 300, color: "#B22222",
            url: fx("type=arrest&level=arresting"), chat: "$user called the cops!" },
        { title: "S1: Pickpocket", prompt: "Lift up to $100 cash off the streamer.", cost: 750, cooldown: 60, color: "#B22222",
            url: fx("type=pickpocket&amount=100"), chat: "$user picked the streamer's pocket." },
        { title: "S1: Hacker", prompt: "Drain up to $500 from the bank account.", cost: 1500, cooldown: 120, color: "#B22222",
            url: fx("type=hack&amount=500"), chat: "$user hacked the bank account." },
        { title: "S1: Rival Gang", prompt: "A rival gang takes up to 10 product off the streamer.", cost: 2000, cooldown: 180, color: "#B22222",
            url: fx("type=rivalgang&amount=10"), chat: "$user sent a rival gang after the stash." },
        { title: "S1: Gang Raid", prompt: `A gang raids the ${property} for up to 20 product.`, cost: 3000, cooldown: 300, color: "#B22222",
            url: fx(`type=raid&property=${prop}&amount=20`), chat: `$user ordered a raid on the ${property}.` }
    ];
}

function buildReward(def, tagId) {
    return {
        manageable: true,
        sortTags: [tagId],
        twitchData: {
            title: def.title,
            prompt: def.prompt,
            cost: def.cost,
            backgroundColor: def.color,
            isEnabled: false,
            isUserInputRequired: false,
            maxPerStreamSetting: { isEnabled: false, maxPerStream: null },
            maxPerUserPerStreamSetting: { isEnabled: false, maxPerUserPerStream: null },
            globalCooldownSetting: { isEnabled: true, globalCooldownSeconds: def.cooldown },
            shouldRedemptionsSkipRequestQueue: true
        },
        restrictionData: {
            restrictions: [],
            mode: "all",
            sendFailMessage: true,
            useCustomFailMessage: false,
            failMessage: "Sorry @{user}, you cannot use this channel reward because: {reason}",
            sendAsReply: false
        },
        effects: effectList([httpEffect(def.url), chatEffect(def.chat)])
    };
}

function buildEvent(id, name, eventId, tagId, category) {
    return {
        id,
        name,
        active: true,
        cached: false,
        sortTags: [],
        sourceId: "twitch",
        eventId,
        filterData: { mode: "exclusive", filters: [] },
        effects: effectList([categoryCheck(tagId, category)])
    };
}

function buildSyncCommand(tagId, category) {
    return {
        id: COMMAND_ID,
        type: "custom",
        trigger: "!s1sync",
        description: "Pause or unpause the Schedule I redeems to match the current stream category.",
        active: true,
        simple: false,
        aliases: [],
        sortTags: [],
        cooldown: {},
        sendCooldownMessage: false,
        cooldownMessage: "This command is still on cooldown for: {timeLeft}",
        sendCooldownMessageAsReply: true,
        treatQuotedTextAsSingleArg: false,
        allowTriggerBySharedChat: "inherit",
        restrictionData: {
            restrictions: [{ id: randomUUID(), type: "firebot:permissions", mode: "roles", roleIds: ["broadcaster", "mod"] }],
            mode: "all",
            sendFailMessage: true
        },
        effects: effectList([
            categoryCheck(tagId, category),
            chatEffect("Schedule I redeems synced to the current category ($category).")
        ])
    };
}

module.exports = {
    getScriptManifest: () => ({
        name: "Schedule I Chat FX redeems setup",
        description: "Creates the Schedule I Chat FX channel rewards (disabled), their sort tag, and the category-aware pause/unpause events.",
        author: "purplehaxttv",
        version: "1.0.0",
        firebotVersion: "5"
    }),

    getDefaultParameters: () => ({
        dryRun: {
            type: "boolean",
            default: true,
            description: "Dry run",
            secondaryDescription: "Change nothing. Only check that everything needed can be reached and write a report next to this script."
        },
        category: {
            type: "string",
            default: "Schedule I",
            description: "Stream category",
            secondaryDescription: "The redeems are unpaused while the stream is in this category and paused otherwise."
        },
        property: {
            type: "string",
            default: "bungalow",
            description: "Property for Supply Drop and Gang Raid",
            secondaryDescription: "Part of the name of the property those two redeems act on."
        },
        baseUrl: {
            type: "string",
            default: "http://localhost:7900",
            description: "Chat FX address",
            secondaryDescription: "Where the Chat FX mod listens (see [ChatFx] Port in the game's MelonPreferences.cfg)."
        }
    }),

    run: async (runRequest) => {
        const { parameters, modules } = runRequest;
        const dryRun = parameters.dryRun !== false;
        const category = (parameters.category || "Schedule I").trim();
        const property = (parameters.property || "bungalow").trim();
        const baseUrl = (parameters.baseUrl || "http://localhost:7900").trim().replace(/\/+$/, "");
        const report = [];
        const say = (line) => { report.push(line); modules.logger.info(`[S1 redeems] ${line}`); };

        try {
            say(`${dryRun ? "DRY RUN" : "LIVE RUN"}: category="${category}", property="${property}", Chat FX at ${baseUrl}`);

            const rewardManager = internal("channel-rewards/channel-reward-manager");
            const sortTagManager = internal("sort-tags/sort-tag-manager").SortTagManager;
            const eventsAccess = internal("events/events-access").EventsAccess;
            if (!rewardManager || typeof rewardManager.saveChannelReward !== "function" || rewardManager.channelRewards == null) {
                throw new Error("could not reach Firebot's channel reward manager");
            }
            if (!sortTagManager || typeof sortTagManager.getSortTagsForContext !== "function") {
                throw new Error("could not reach Firebot's sort tag manager");
            }
            if (!eventsAccess || typeof eventsAccess.saveNewEventToMainEvents !== "function") {
                throw new Error("could not reach Firebot's event store");
            }
            const existingRewards = Object.values(rewardManager.channelRewards);
            say(`Firebot currently knows ${existingRewards.length} channel rewards (${existingRewards.filter(r => r.manageable).length} editable in Firebot).`);

            // sort tag
            const tags = sortTagManager.getSortTagsForContext(TAG_CONTEXT) || [];
            let tag = tags.find(t => t.name === TAG_NAME);
            if (tag) {
                say(`Sort tag "${TAG_NAME}" already exists.`);
            } else if (dryRun) {
                tag = { id: "(new)", name: TAG_NAME };
                say(`Would create sort tag "${TAG_NAME}".`);
            } else {
                tag = { id: randomUUID(), name: TAG_NAME };
                sortTagManager.saveSortTagsForContext(TAG_CONTEXT, [...tags, tag]);
                sortTagManager.triggerUiRefresh();
                say(`Created sort tag "${TAG_NAME}".`);
            }

            // rewards
            let created = 0;
            for (const def of rewardDefinitions(baseUrl, property)) {
                const existing = existingRewards.find(r => r.twitchData && r.twitchData.title === def.title);
                if (existing) {
                    say(`Reward "${def.title}" already exists, left as it is.`);
                    continue;
                }
                if (dryRun) {
                    say(`Would create reward "${def.title}" (cost ${def.cost}, cooldown ${def.cooldown}s, disabled) -> ${def.url}`);
                    continue;
                }
                const saved = await rewardManager.saveChannelReward(buildReward(def, tag.id), true);
                if (saved == null) {
                    say(`FAILED to create reward "${def.title}" (see the Firebot log for Twitch's reason).`);
                } else {
                    created++;
                    say(`Created reward "${def.title}" (disabled, cost ${def.cost}).`);
                }
            }
            if (!dryRun && typeof rewardManager.triggerUiRefresh === "function") {
                rewardManager.triggerUiRefresh();
            }

            // events and sync command
            if (dryRun) {
                say(`Would add events "Schedule I redeems: category changed" and "Schedule I redeems: stream started", and the command !s1sync.`);
            } else {
                eventsAccess.saveNewEventToMainEvents(buildEvent(EVENT_CATEGORY_ID, "Schedule I redeems: category changed", "category-changed", tag.id, category));
                eventsAccess.saveNewEventToMainEvents(buildEvent(EVENT_ONLINE_ID, "Schedule I redeems: stream started", "stream-online", tag.id, category));
                eventsAccess.triggerUiRefresh();
                modules.commandManager.saveImportedCustomCommand(buildSyncCommand(tag.id, category));
                modules.commandManager.triggerUiRefresh();
                say(`Saved the two category events and the !s1sync command.`);
                say(`Done: ${created} reward(s) created, all disabled. Review them under Channel Rewards, then enable the ones you want.`);
            }
        } catch (err) {
            say(`ERROR: ${err && err.message ? err.message : err}`);
        }

        try {
            fs.writeFileSync(path.join(__dirname, "schedule1-redeems-report.txt"), report.join("\n") + "\n", "utf8");
        } catch (err) {
            modules.logger.warn(`[S1 redeems] could not write the report: ${err.message}`);
        }
        return { success: true, effects: [] };
    }
};
