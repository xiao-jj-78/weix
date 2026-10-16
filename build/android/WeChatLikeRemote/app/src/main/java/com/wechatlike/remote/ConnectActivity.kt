/**
 * 微信点赞遥控器 - Android 手机端（零第三方依赖）。
 *
 * 与电脑端「微信自动化控制台」通过局域网 HTTP 通信：
 *   GET  /api/status  任务状态
 *   POST /api/task/start / /api/task/stop
 *   GET  /api/logs    运行日志
 */
package com.wechatlike.remote

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView
import android.app.Activity

class ConnectActivity : Activity() {

    private lateinit var ipInput: EditText
    private lateinit var portInput: EditText
    private lateinit var hint: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val prefs = getSharedPreferences("wxremote", Context.MODE_PRIVATE)
        val layout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(48, 96, 48, 48)
        }

        TextView(this).apply {
            text = "微信点赞遥控器"
            textSize = 24f
            setPadding(0, 0, 0, 32)
            layout.addView(this)
        }
        TextView(this).apply {
            text = "请输入电脑端控制台显示的地址\n（手机与电脑需连接同一 Wi-Fi）"
            textSize = 14f
            setPadding(0, 0, 0, 32)
            layout.addView(this)
        }

        ipInput = EditText(this).apply {
            hint = "电脑 IP，如 192.168.1.5"
            setText(prefs.getString("ip", "") ?: "")
            setSingleLine()
            layout.addView(this)
        }
        portInput = EditText(this).apply {
            hint = "端口，默认 8765"
            setText(prefs.getString("port", "8765") ?: "8765")
            setSingleLine()
            layout.addView(this)
        }

        Button(this).apply {
            text = "连 接"
            setOnClickListener { connect() }
            layout.addView(this)
        }

        hint = TextView(this).apply {
            textSize = 13f
            setPadding(0, 24, 0, 0)
            layout.addView(this)
        }

        setContentView(layout)
    }

    private fun connect() {
        val ip = ipInput.text.toString().trim()
        val port = portInput.text.toString().trim().ifEmpty { "8765" }
        if (ip.isEmpty()) {
            hint.text = "请先输入电脑 IP"
            return
        }
        hint.text = "正在连接 $ip:$port …"
        Api.ping(ip, port) { ok, msg ->
            runOnUiThread {
                if (ok) {
                    getSharedPreferences("wxremote", Context.MODE_PRIVATE).edit()
                        .putString("ip", ip)
                        .putString("port", port)
                        .apply()
                    startActivity(
                        Intent(this, ControlActivity::class.java)
                            .putExtra("ip", ip)
                            .putExtra("port", port)
                    )
                } else {
                    hint.text = "连接失败：$msg\n请检查：1) 电脑端已启动遥控服务 2) 同一 Wi-Fi 3) IP 端口正确"
                }
            }
        }
    }
}
