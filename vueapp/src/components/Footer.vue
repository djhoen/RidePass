<template>
    <v-footer class="bg-grey-darken-4 text-white pa-6">
        <v-container>
            <v-row>
                <v-col cols="12" md="4">
                    <h3>Template App</h3>
                    <p class="m-t-10 font-inter text-grey-lighten-1">Your application description goes here.</p>
                </v-col>
                <v-col cols="12" md="4">
                    <h4>Quick Links</h4>
                    <div class="m-t-10">
                        <div><router-link to="/" class="footer-link">Home</router-link></div>
                        <div><router-link to="/BlogFeed" class="footer-link">Blog</router-link></div>
                        <div><router-link to="/Faqs" class="footer-link">FAQs</router-link></div>
                    </div>
                </v-col>
                <v-col cols="12" md="4">
                    <h4>Contact</h4>
                    <div class="m-t-10 font-inter text-grey-lighten-1">
                        <div><v-icon size="small" class="m-r-5">mdi-email</v-icon>support@yourdomain.com</div>
                    </div>
                </v-col>
            </v-row>
            <v-divider class="m-t-20 m-b-10"></v-divider>
            <v-row>
                <v-col class="text-center text-grey-lighten-1 font-inter">
                    &copy; {{ new Date().getFullYear() }} Template App. All rights reserved.
                </v-col>
            </v-row>
        </v-container>
    </v-footer>
</template>

<style scoped>
.footer-link {
    color: #9e9e9e;
    text-decoration: none;
    line-height: 2;
}
.footer-link:hover {
    color: white;
}
</style>
