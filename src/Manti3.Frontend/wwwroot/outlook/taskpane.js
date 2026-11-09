// Side panel of the MANTI3 Outlook add-in. Opens when the ribbon button is pressed: reads the mail that is open /
// selected, sends it to the backend (which starts the agent in the background), then moves this panel to the
// access-requests page for that run (progress, explanation, "run script").
Office.onReady(function () {
    getMail()
        .then(startJob)
        .then(function (jobId) {
            location.replace(location.origin + "/access-requests?job=" + encodeURIComponent(jobId));
        })
        .catch(function (err) {
            showError(err && err.message ? err.message : "לא ניתן להפעיל את הסוכן על המייל הזה.");
        });
});

function showError(message) {
    document.getElementById("spinner").style.display = "none";
    var status = document.getElementById("status");
    status.className = "error";
    status.textContent = message + " סגרו את החלונית ולחצו שוב על הכפתור.";
}

// Preferred: the whole mail as an .eml file (Mailbox 1.14). Fallback: subject, sender, date and the plain-text body.
function getMail() {
    var item = Office.context.mailbox.item;
    return new Promise(function (resolve, reject) {
        if (!item) {
            reject({ message: "לא נבחר מייל." });
            return;
        }

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

function startJob(payload) {
    return fetch("../appsettings.json")
        .then(function (r) { return r.json(); })
        .then(function (config) {
            var api = (config.ApiBaseUrl || "").replace(/\/?$/, "/");
            return fetch(api + "api/access-requests/jobs", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            }).catch(function () {
                throw { message: "שרת MANTI3 (ה-API) לא זמין בכתובת " + api + "." };
            });
        })
        .then(function (response) {
            if (!response.ok) {
                throw { message: "שליחת המייל לסוכן נכשלה (" + response.status + ")." };
            }
            return response.json();
        })
        .then(function (job) { return job.jobId; });
}
