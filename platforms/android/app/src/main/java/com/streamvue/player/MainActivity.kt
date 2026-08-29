package com.streamvue.player

import android.content.res.Configuration
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.compose.runtime.getValue
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.lifecycleScope
import com.streamvue.player.premium.GooglePlayPremiumBilling
import com.streamvue.player.ui.StreamVueApp
import com.streamvue.player.ui.theme.StreamVueTheme
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    private val viewModel: MainViewModel by viewModels()
    private lateinit var premiumBilling: GooglePlayPremiumBilling

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        premiumBilling = GooglePlayPremiumBilling(applicationContext)
        lifecycleScope.launch {
            premiumBilling.state.collectLatest(viewModel::updatePremiumBilling)
        }

        val isTelevision = resources.configuration.uiMode and Configuration.UI_MODE_TYPE_MASK ==
            Configuration.UI_MODE_TYPE_TELEVISION

        setContent {
            val state by viewModel.state.collectAsStateWithLifecycle()
            val documentPicker = rememberLauncherForActivityResult(
                contract = ActivityResultContracts.OpenDocument()
            ) { uri ->
                uri?.let(viewModel::importDocument)
            }

            StreamVueTheme {
                StreamVueApp(
                    state = state,
                    isTelevision = isTelevision,
                    onChooseFile = {
                        documentPicker.launch(
                            arrayOf(
                                "application/vnd.apple.mpegurl",
                                "application/x-mpegurl",
                                "audio/mpegurl",
                                "text/plain",
                                "*/*"
                            )
                        )
                    },
                    onImportUrl = viewModel::importUrl,
                    onConnectPlex = viewModel::connectPlex,
                    onConnectEmby = viewModel::connectEmby,
                    onPurchasePremium = { premiumBilling.launchPurchase(this@MainActivity) },
                    onRestorePremium = premiumBilling::restorePurchases,
                    onRefresh = viewModel::refresh,
                    onSelectGroup = viewModel::selectGroup,
                    onQueryChanged = viewModel::updateQuery,
                    onSelectChannel = viewModel::selectChannel,
                    onDismissNotice = viewModel::dismissNotice,
                    onDismissError = viewModel::dismissError,
                    onFullscreenChanged = ::setFullscreen
                )
            }
        }
    }

    override fun onStart() {
        super.onStart()
        premiumBilling.start()
    }

    override fun onResume() {
        super.onResume()
        premiumBilling.refresh()
    }

    override fun onDestroy() {
        premiumBilling.close()
        super.onDestroy()
    }

    private fun setFullscreen(enabled: Boolean) {
        WindowCompat.getInsetsController(window, window.decorView).apply {
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            if (enabled) {
                hide(WindowInsetsCompat.Type.systemBars())
            } else {
                show(WindowInsetsCompat.Type.systemBars())
            }
        }
    }
}
