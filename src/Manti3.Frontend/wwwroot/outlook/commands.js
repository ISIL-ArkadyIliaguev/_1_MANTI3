// Outlook add-in command: the ribbon button "הרשאות" takes the mail that is open/selected, sends it to the MANTI3
// backend (which starts the access-request agent in the background), and opens the MANTI3 page in a popup on that
// request. The popup shows progress, then the explanation and the "run script" choice. No files to add.
//
// The popup must be HTTPS and on the same domain as this file (an Office add-in rule), so its address is taken from
// the origin this file was loaded from: https://localhost:7296 in development.
Office.onReady(function () { });

// The popup is opened FIRST, straight from the click, with a request id made here (promptBeforeOpen:false is only
// allowed by the browser while the call is still part of the click). Then the mail is read and sent to the backend
// under that id; the popup waits until the request appears. If anything fails, the popup is closed and the error is
// shown on the mail.
function openManti(event) {
    var dialog = null;
    var stage = "start";
    var finished = false;

    // Outlook shows "is working on your request" until event.completed() is called, so it must always be called:
    // also when something throws synchronously, and also when a step never answers (a watchdog).
    function finish() {
        if (finished) { return; }
        finished = true;
        clearTimeout(watchdog);
        event.completed();
    }

    var watchdog = setTimeout(function () {
        showError("MANTI3: הפעולה נתקעה בשלב '" + stage + "'. סגרו והפעילו שוב; אם זה חוזר, בדקו שהחזית והשרת רצים.");
        finish();
    }, 30000);

    try {
        var jobId = newJobId();
        stage = "popup";
        openDialog(jobId)
            .then(function (d) { dialog = d; stage = "mail"; return getMail(); })
            .then(function (payload) { stage = "server"; return startJob(payload, jobId); })
            .catch(function (err) {
                if (dialog) { try { dialog.close(); } catch (e) { /* already closed */ } }
                showError(describeError(err));
            })
            .then(finish);
    } catch (err) {
        showError("MANTI3: שגיאה בשלב '" + stage + "': " + (err && err.message ? err.message : err));
        finish();
    }
}

// 32 random hex characters (128 bits): the backend accepts only this shape
function newJobId() {
    var bytes = new Uint8Array(16);
    crypto.getRandomValues(bytes);
    return Array.prototype.map.call(bytes, function (b) { return ("0" + b.toString(16)).slice(-2); }).join("");
}

// ---- 1. the open mail ------------------------------------------------------------------------------------------

// Preferred: the whole mail as an .eml file (Mailbox 1.14: new Outlook, Outlook on the web, recent classic builds).
// Fallback: subject, sender, date and the plain-text body.
function getMail() {
    var item = Office.context.mailbox.item;
    return new Promise(function (resolve, reject) {
        var canGetFile = item.getAsFileAsync && Office.context.requirements.isSetSupported("Mailbox", "1.14");
        if (!canGetFile) {
            textFallback(item).then(resolve, reject);
            return;
        }

        item.getAsFileAsync(function (result) {
            if (result.status === Office.AsyncResultStatus.Succeeded) {
                resolve({ mailFiles: [{ name: "mail.eml", base64: result.value }] });
            } else {
                textFallback(item).then(resolve, reject);
            }
        });
    });
}

function textFallback(item) {
    return new Promise(function (resolve, reject) {
        item.body.getAsync(Office.CoercionType.Text, function (result) {
            if (result.status !== Office.AsyncResultStatus.Succeeded) {
                reject({ message: "לא ניתן לקרוא את תוכן המייל (" + result.error.code + ")." });
                return;
            }

            var from = item.from && (item.from.emailAddress || item.from.displayName);
            var text = (from ? "From: " + from + "\n" : "") +
                (item.dateTimeCreated ? "Date: " + item.dateTimeCreated.toISOString() + "\n" : "") +
                "Subject: " + (item.subject || "") + "\n\n" + result.value;
            resolve({ emailText: text });
        });
    });
}

// ---- 2. start the agent on the backend -------------------------------------------------------------------------

function startJob(payload, jobId) {
    return fetch("../appsettings.json")
        .then(function (r) { return r.json(); })
        .then(function (config) {
            var api = (config.ApiBaseUrl || "").replace(/\/?$/, "/");
            return fetch(api + "api/access-requests/jobs?id=" + encodeURIComponent(jobId), {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            }).catch(function () {
                throw { message: "שרת MANTI3 (ה-API) לא זמין בכתובת " + api };
            });
        })
        .then(function (response) {
            if (!response.ok) {
                throw { message: "שליחת המייל לסוכן נכשלה (" + response.status + ")." };
            }
            return response.json();
        })
        .then(function () { /* started */ });
}

// ---- 3. the popup ----------------------------------------------------------------------------------------------

// promptBeforeOpen stays at its default (true). Tested in the new Outlook: with promptBeforeOpen:false the browser
// blocks the popup (error 12011) even when it is opened first thing in the command, because a ribbon command is not
// a real click. So Outlook's small "wants to display a new window" question (Allow) cannot be removed here.
function openDialog(jobId) {
    var url = location.origin + "/access-requests?job=" + encodeURIComponent(jobId);
    return new Promise(function (resolve, reject) {
        Office.context.ui.displayDialogAsync(url, { height: 90, width: 80, displayInIframe: false }, function (result) {
            if (result.status === Office.AsyncResultStatus.Failed) {
                reject({ code: result.error.code });
            } else {
                result.value.addEventHandler(Office.EventType.DialogEventReceived, function () { });
                resolve(result.value);
            }
        });
    });
}

// ---- errors ----------------------------------------------------------------------------------------------------

function describeError(err) {
    if (err && err.message) {
        return err.message;
    }

    switch (err && err.code) {
        case 12011:
        case 12009:
            return "הדפדפן חסם את החלון הקופץ (" + err.code + "). לחצו שוב על הכפתור ובחרו 'אפשר' ('Allow'), או אפשרו חלונות קופצים עבור " + location.origin;
        case 12007:
            return "חלון MANTI3 כבר פתוח (" + err.code + ").";
        case 12004:
        case 12005:
            return "הכתובת של MANTI3 חייבת להיות HTTPS ובאותו דומיין של התוסף (" + err.code + "): " + location.origin;
        default:
            return "לא ניתן לפתוח את MANTI3" + (err && err.code ? " (" + err.code + ")" : "") + ". בדקו שהחזית רצה בכתובת " + location.origin;
    }
}

function showError(message) {
    try {
        Office.context.mailbox.item.notificationMessages.addAsync("mantiError", {
            type: "errorMessage",
            message: message
        });
    } catch (e) { /* no item context: nothing to show */ }
}

// the name must match <FunctionName> in manifest.xml
Office.actions.associate("openManti", openManti);
