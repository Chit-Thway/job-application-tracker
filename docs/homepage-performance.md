# Homepage performance release

Measured on 21 November 2026. The Azure changes shipped in [PR #50](https://github.com/Chit-Thway/job-application-tracker/pull/50) with [production release `a2d938c`](https://github.com/Chit-Thway/job-application-tracker/actions/runs/44901070686).

- The homepage now serves a 48 KB desktop WebP or a 25 KB mobile WebP in place of the 112 KB dashboard PNG. The demo shows a lightweight poster and buffers after the dashboard image paints. Play starts loading immediately if pressed earlier, and Data Saver skips background buffering.
- Production releases now provision and verify the configured administrator before deploying the new revision. A local fresh-process benchmark went from 1.85 s to 0.67 s for the first homepage response, and startup database queries dropped from four to zero.
- The Cloudflare preview serves the signed-out page and its assets independently of Azure. The Worker routes requests carrying an app session to the application on the production hostname and supports byte-range video responses.

| Time until the dashboard image appeared | Original site | Updated main domain | Cloudflare preview |
|---|---:|---:|---:|
| Desktop repeat visit | 2.61 s | 0.61 s, 76% sooner | 0.39 s |
| Simulated mobile repeat visit | 1.76 s | 0.86 s, 51% sooner | 0.87 s |
| Desktop with Azure at zero replicas | 25.08 s | No confirmed sample | 0.39 s |

Measurements used fresh Chromium browser sessions. The repeat-visit values are medians from two or three visits per device. Simulated mobile used a 1.6 Mbps connection. The original cold result was one visit. The preview cold result was the median of three desktop visits, with Azure at zero replicas before and after the test set. The preview median was 98% below the original cold sample. A separate confirmed cold sign-in on the updated Azure revision took 26.15 s to first byte.

Cloudflare became authoritative for the main domain on 22 November 2026. A confirmed zero-replica request to the live Azure homepage took 23.49 s to first byte, including 6.41 s between Azure's image pull events. The official Linux x64 chiseled ASP.NET base image is 72.6 MB compressed, down from 104.0 MB for the standard base image. Measure the cold request again after releasing the smaller image to determine the end-to-end improvement.

The 0.39 s cold-homepage result applies to the separate preview. Serving it on the main domain requires an origin certificate compatible with Cloudflare proxying and activation of the Worker Route. [Deployment guide](azure-deployment.md#main-domain-rollout)

An Australia East staging rehearsal used the same release as production and routed through Cloudflare's Perth edge. One signed-out request returned the first byte in 2.67 s with Azure at zero replicas. Azure stayed at zero after fetching the dashboard WebP and a partial video response. The sign-in form loaded separately, and a synthetic auth-cookie request went to Azure. The rehearsal used a temporary origin certificate and fictional database settings.
